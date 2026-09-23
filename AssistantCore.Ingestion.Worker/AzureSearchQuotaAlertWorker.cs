using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Services.AzureSearch;
using AssistantCore.Service.Application.Services.Incidents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantCore.Ingestion.Worker;

/// <summary>
/// Alerte quand le quota de stockage du service Azure AI Search (partage par tous les
/// index du service, pas un concept que cette application suit elle-meme) franchit le
/// seuil configure. Verifie des le demarrage plutot que d'attendre le premier intervalle,
/// pour signaler immediatement un depassement deja en cours.
/// </summary>
public sealed class AzureSearchQuotaAlertWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<AzureSearchQuotaAlertOptions> options,
    ILogger<AzureSearchQuotaAlertWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Azure AI Search quota alert worker is disabled by configuration.");
            return;
        }

        var interval = TimeSpan.FromMinutes(settings.IntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckQuotaAsync(settings, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Azure AI Search quota check cycle failed; retrying next cycle.");
                await ReportWorkerIncidentAsync(exception, stoppingToken);
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CheckQuotaAsync(
        AzureSearchQuotaAlertOptions settings,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var quotaChecker = scope.ServiceProvider.GetRequiredService<IAzureSearchQuotaChecker>();
        var alertGate = scope.ServiceProvider.GetRequiredService<IAzureSearchQuotaAlertGate>();
        var incidentReporter = scope.ServiceProvider.GetRequiredService<IOperationalIncidentReporter>();

        var status = await quotaChecker.GetQuotaStatusAsync(cancellationToken);

        logger.LogInformation(
            "Azure AI Search storage usage is at {UsageRatio:P0} ({UsageBytes} / {QuotaBytes} bytes).",
            status.UsageRatio,
            status.StorageUsageInBytes,
            status.StorageQuotaInBytes);

        if (status.UsageRatio < settings.ThresholdRatio || !alertGate.TryAcquire())
        {
            return;
        }

        await incidentReporter.ReportAsync(
            new OperationalIncidentReport(
                new AzureSearchQuotaAlertException(
                    status.UsageRatio,
                    status.StorageUsageInBytes,
                    status.StorageQuotaInBytes),
                $"search-quota-{Guid.NewGuid():N}"),
            cancellationToken);
    }

    // Genere son propre correlationId : ce process n'a pas de HttpContext. Entierement
    // enveloppe dans son propre try/catch : une panne d'observabilite ne doit jamais
    // faire planter la boucle du worker.
    private async Task ReportWorkerIncidentAsync(Exception exception, CancellationToken cancellationToken)
    {
        try
        {
            var correlationId = $"worker-{Guid.NewGuid():N}";
            logger.LogError(
                "Operational incident captured for worker cycle. CorrelationId: {CorrelationId}",
                correlationId);

            await using var incidentScope = scopeFactory.CreateAsyncScope();
            var reporter = incidentScope.ServiceProvider.GetService<IOperationalIncidentReporter>();
            if (reporter is null)
            {
                return;
            }

            await reporter.ReportAsync(
                new OperationalIncidentReport(
                    exception,
                    correlationId,
                    SubsystemOverride: OperationalIncidentSubsystem.WorkerJobs),
                cancellationToken);
        }
        catch (Exception reportingException)
        {
            logger.LogError(reportingException, "Failed to report an operational incident for a worker cycle.");
        }
    }
}
