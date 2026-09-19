using AssistantCore.Repository.Domain.Entities;

namespace AssistantCore.Repository.Repositories;

public interface IMicrosoft365ListItemWorkProcessingRepository
{
    Task<Microsoft365ListItemWork?> ClaimNextAsync(
        Guid leaseId,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        Microsoft365ListItemWork work,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);

    Task FailAsync(
        Microsoft365ListItemWork work,
        bool isPermanent,
        string errorCode,
        DateTimeOffset failedAt,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken = default);
}
