using AssistantCore.ExternalServices.Services.Email;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Queries;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Services.Incidents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantCore.Ingestion.Worker;

/// <summary>
/// Envoie periodiquement un resume par courriel des incidents operationnels
/// (#15) survenus depuis le dernier envoi. Chaque cycle ne couvre que la fenetre
/// [maintenant - IntervalMinutes, maintenant), jamais un incident deux fois tant
/// que le worker reste en vie entre deux cycles.
/// </summary>
public sealed class OperationalIncidentDigestWorker(
    IServiceScopeFactory scopeFactory,
    IEmailSender emailSender,
    IOptions<OperationalIncidentDigestOptions> options,
    TimeProvider timeProvider,
    IHostEnvironment environment,
    ILogger<OperationalIncidentDigestWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Operational incident digest worker is disabled by configuration.");
            return;
        }

        var interval = TimeSpan.FromMinutes(settings.IntervalMinutes);
        var windowStart = timeProvider.GetUtcNow();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var windowEnd = timeProvider.GetUtcNow();
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var queries = scope.ServiceProvider.GetRequiredService<IOperationalIncidentQueries>();
                await SendDigestIfAnyAsync(queries, settings.RecipientAddress, windowStart, windowEnd, stoppingToken);
                windowStart = windowEnd;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Operational incident digest cycle failed; the same window will be retried next cycle.");
                await ReportWorkerIncidentAsync(exception, stoppingToken);
            }
        }
    }

    private async Task SendDigestIfAnyAsync(
        IOperationalIncidentQueries queries,
        string recipientAddress,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken)
    {
        var page = await queries.SearchAsync(
            organizationId: null,
            from: windowStart,
            to: windowEnd,
            subsystem: null,
            severity: null,
            correlationId: null,
            page: 1,
            pageSize: 100,
            cancellationToken: cancellationToken);

        if (page.TotalCount == 0)
        {
            return;
        }

        var body = BuildDigestBody(page, windowStart, windowEnd, environment.EnvironmentName);
        await emailSender.SendAsync(
            new EmailMessage(
                recipientAddress,
                $"onPremia [{environment.EnvironmentName}] - {page.TotalCount} incident(s) operationnel(s)",
                body),
            cancellationToken);

        logger.LogInformation(
            "Sent an operational incident digest with {IncidentCount} incident(s) to {RecipientAddress}.",
            page.TotalCount,
            recipientAddress);
    }

    private static string BuildDigestBody(
        OperationalIncidentListPageData page,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        string environmentName)
    {
        var lines = new List<string>
        {
            $"Environnement : {environmentName}",
            $"Incidents operationnels entre {windowStart:u} et {windowEnd:u} ({page.TotalCount} au total) :",
            string.Empty
        };

        // Les plus critiques en premier : c'est ce qu'un lecteur presse veut voir
        // sans avoir a parcourir toute la liste.
        var sortedIncidents = page.Items
            .OrderByDescending(incident => incident.Severity)
            .ThenBy(incident => incident.OccurredAt);

        foreach (var incident in sortedIncidents)
        {
            lines.Add(
                $"- [{incident.Severity}] {incident.Subsystem} ({incident.OccurredAt:u}) : {incident.Summary} " +
                $"(correlationId={incident.CorrelationId}, organisation={incident.OrganizationName ?? "n/a"})");
        }

        return string.Join(Environment.NewLine, lines);
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
