using AssistantCore.Repository.Domain.Entities;

namespace AssistantCore.Repository.Repositories;

public interface IMicrosoft365DocumentWorkProcessingRepository
{
    Task<Microsoft365DocumentWork?> ClaimNextAsync(
        Guid leaseId,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        Microsoft365DocumentWork work,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);

    Task FailAsync(
        Microsoft365DocumentWork work,
        bool isPermanent,
        string errorCode,
        DateTimeOffset failedAt,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken = default);
}
