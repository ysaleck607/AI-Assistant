using AssistantCore.Service.Application.Models.Microsoft365;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public interface IMicrosoft365DriveSynchronizationService
{
    /// <param name="reindexOperationId">
    /// Reprise administrative a l'origine de cette lecture complete, lorsqu'il y en a une.
    /// </param>
    Task<Microsoft365DriveInitialSynchronizationResult> StartInitialSynchronizationAsync(
        Guid sourceId,
        Guid synchronizationId,
        Guid? reindexOperationId = null,
        CancellationToken cancellationToken = default);

    Task<Microsoft365DriveDeltaSynchronizationResult> StartDeltaSynchronizationAsync(
        Guid sourceId,
        Guid synchronizationId,
        CancellationToken cancellationToken = default);
}
