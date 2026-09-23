using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Services.Conversations.Purge;
using AssistantCore.Service.Application.Services.Incidents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantCore.Ingestion.Worker;

/// <summary>
/// Execute la purge physique des conversations supprimees, hors de tout
/// controller HTTP : une purge peut durer et ne doit pas dependre d'une requete
/// utilisateur.
///
/// Le worker enchaine les demandes echues tant qu'il en trouve, puis attend
/// avant de balayer a nouveau. Chaque demande est traitee dans sa propre portee
/// afin qu'un echec n'emporte pas le contexte des suivantes.
/// </summary>
public sealed class ConversationPurgeWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ConversationPurgeOptions> options,
    ILogger<ConversationPurgeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Conversation purge worker is disabled by configuration.");
            return;
        }

        var idleDelay = TimeSpan.FromSeconds(settings.PollingIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            bool purgedSomething;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var purgeService = scope.ServiceProvider
                    .GetRequiredService<IConversationPurgeService>();
                purgedSomething = await purgeService.PurgeNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Le service enregistre deja l'echec d'une demande. Une exception
                // qui remonte jusqu'ici vient de la reclamation elle-meme, par
                // exemple une base indisponible : le worker attend plutot que de
                // boucler a vide, et surtout il ne s'arrete pas.
                logger.LogWarning(
                    "Conversation purge sweep failed ({FailureType}); retrying after the polling interval.",
                    exception.GetType().Name);
                await ReportWorkerIncidentAsync(exception, stoppingToken);
                purgedSomething = false;
            }

            if (purgedSomething) continue;

            try
            {
                await Task.Delay(idleDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // Genere son propre correlationId : ce process n'a pas de HttpContext, donc pas de
    // TraceIdentifier a reutiliser comme le fait ExceptionMiddleware cote HTTP. Entierement
    // enveloppe dans son propre try/catch : une panne d'observabilite (y compris la
    // resolution du service, qui peut echouer avant meme d'atteindre OperationalIncidentReporter)
    // ne doit jamais faire planter la boucle du worker.
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
