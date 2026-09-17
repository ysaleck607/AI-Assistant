using AssistantCore.Repository.Domain.Entities;

namespace AssistantCore.Repository.Repositories;

public interface IMicrosoft365OutlookSynchronizationRepository
{
    Task<Microsoft365Source?> FindForSynchronizationAsync(
        Guid sourceId,
        CancellationToken cancellationToken = default);
}
