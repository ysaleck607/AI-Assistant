namespace AssistantCore.Service.Application.Services.Incidents;

/// <summary>
/// Point d'appel unique utilise par ExceptionMiddleware (chemin HTTP) et par les
/// BackgroundService de AssistantCore.Ingestion.Worker (chemin job).
/// </summary>
public interface IOperationalIncidentReporter
{
    Task ReportAsync(OperationalIncidentReport report, CancellationToken cancellationToken = default);
}
