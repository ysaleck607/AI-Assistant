using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Services.Incidents;
using AssistantCore.Service.Application.Services.LlmQuota;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantCore.Ingestion.Worker;

/// <summary>
/// Alerte quand la consommation mensuelle (suivie par cette application, voir
/// ILlmTokenConsumptionTracker) d'un modele franchit le seuil configure. Deux modeles
/// suivis independamment : le modele de chat/planification et le modele d'embedding.
/// Verifie des le demarrage plutot que d'attendre le premier intervalle.
/// </summary>
public sealed class LlmQuotaAlertWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<LlmQuotaAlertOptions> options,
    IOptions<AzureAiSearchOptions> searchOptions,
    IOptions<Microsoft365Options> microsoft365Options,
    ILogger<LlmQuotaAlertWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("LLM quota alert worker is disabled by configuration.");
            return;
        }

        var interval = TimeSpan.FromMinutes(settings.IntervalMinutes);
        var trackedModels = new[]
        {
            (Model: searchOptions.Value.PlanningModelName, Limit: settings.ChatModelMonthlyTokenLimit),
            (Model: microsoft365Options.Value.EmbeddingModel, Limit: settings.EmbeddingModelMonthlyTokenLimit)
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var (model, limit) in trackedModels)
            {
                // Limite non configuree (0) : verification sautee plutot que fausse alerte.
                if (string.IsNullOrWhiteSpace(model) || limit <= 0)
                {
                    continue;
                }

                try
                {
                    await CheckModelQuotaAsync(model, limit, settings.ThresholdRatio, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "LLM quota check cycle failed for {Model}; retrying next cycle.", model);
                    await ReportWorkerIncidentAsync(exception, stoppingToken);
                }
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

    private async Task CheckModelQuotaAsync(
        string model,
        long limit,
        double thresholdRatio,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var tracker = scope.ServiceProvider.GetRequiredService<ILlmTokenConsumptionTracker>();
        var alertGate = scope.ServiceProvider.GetRequiredService<ILlmQuotaAlertGate>();
        var incidentReporter = scope.ServiceProvider.GetRequiredService<IOperationalIncidentReporter>();

        var consumed = await tracker.GetCurrentPeriodConsumptionAsync(model, cancellationToken);
        var ratio = (double)consumed / limit;

        logger.LogInformation(
            "LLM token consumption for {Model} is at {UsageRatio:P0} this period ({Consumed} / {Limit} tokens).",
            model,
            ratio,
            consumed,
            limit);

        if (ratio < thresholdRatio || !alertGate.TryAcquire(model))
        {
            return;
        }

        await incidentReporter.ReportAsync(
            new OperationalIncidentReport(
                new LlmQuotaAlertException(model, ratio, consumed, limit),
                $"llm-quota-{model}-{Guid.NewGuid():N}"),
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
