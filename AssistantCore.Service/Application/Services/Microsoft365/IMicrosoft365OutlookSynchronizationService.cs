namespace AssistantCore.Service.Application.Services.Microsoft365;

public interface IMicrosoft365OutlookSynchronizationService
{
    Task StartInitialSynchronizationAsync(
        Guid sourceId,
        Guid synchronizationId,
        CancellationToken cancellationToken = default);

    Task StartDeltaSynchronizationAsync(
        Guid sourceId,
        Guid synchronizationId,
        CancellationToken cancellationToken = default);
}
