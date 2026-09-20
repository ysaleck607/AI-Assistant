using AssistantCore.Repository.Repositories;
using AssistantCore.Repository.Repositories.Incidents;
using Microsoft.Extensions.Logging;

namespace AssistantCore.Service.Application.Services.Incidents;

public sealed class OperationalIncidentReporter(
    IOperationalIncidentRepository repository,
    ISensitiveDataRedactor redactor,
    TimeProvider timeProvider,
    ILogger<OperationalIncidentReporter> logger) : IOperationalIncidentReporter
{
    private const int MaxSummaryLength = 300;

    public async Task ReportAsync(
        OperationalIncidentReport report,
        CancellationToken cancellationToken = default)
    {
        // Une panne d'ecriture d'incident ne doit jamais casser la reponse HTTP ni la boucle
        // du worker qui a declenche l'appel : uniquement loggee.
        try
        {
            var (classifiedSubsystem, severity) = OperationalIncidentSubsystemClassifier.Classify(report.Exception);
            var subsystem = report.SubsystemOverride ?? classifiedSubsystem;

            var incident = OperationalIncidentFactory.Create(
                subsystem,
                severity,
                report.CorrelationId,
                report.OrganizationId,
                report.OrganizationMemberId,
                redactor.Redact(BuildSummary(report.Exception)),
                redactor.Redact(BuildSafeDetail(report.Exception)),
                report.RelatedResourceType,
                report.RelatedResourceId,
                timeProvider.GetUtcNow());

            await repository.PersistAsync(incident, cancellationToken);
        }
        catch (Exception reportingException)
        {
            logger.LogError(reportingException, "Failed to persist an operational incident.");
        }
    }

    private static string BuildSummary(Exception exception)
    {
        var summary = $"{exception.GetType().Name}: {exception.Message}";
        return summary.Length > MaxSummaryLength ? summary[..MaxSummaryLength] : summary;
    }

    // Jamais .StackTrace ni .ToString() : uniquement les messages (type + un niveau
    // d'InnerException), pour ne jamais exposer un chemin de fichier ou une variable locale.
    private static string BuildSafeDetail(Exception exception)
    {
        var detail = $"{exception.GetType().Name}: {exception.Message}";

        if (exception.InnerException is not null)
        {
            detail += $" -> {exception.InnerException.GetType().Name}: {exception.InnerException.Message}";
        }

        return detail;
    }
}
