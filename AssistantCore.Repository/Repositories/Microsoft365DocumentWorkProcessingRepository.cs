using System.Data;
using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Repository.Repositories;

public sealed class Microsoft365DocumentWorkProcessingRepository(AssistantCoreDbContext dbContext)
    : IMicrosoft365DocumentWorkProcessingRepository
{
    public async Task<Microsoft365DocumentWork?> ClaimNextAsync(
        Guid leaseId,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var work = await dbContext.Microsoft365DocumentWorks
            .Include(candidate => candidate.Organization)
            .Include(candidate => candidate.Microsoft365Source)
                .ThenInclude(source => source.Microsoft365Connection)
            .Where(candidate =>
                candidate.Status == Microsoft365DocumentWorkStatus.Pending
                || candidate.Status == Microsoft365DocumentWorkStatus.TemporaryFailure
                    && (candidate.NextAttemptAt == null || candidate.NextAttemptAt <= now)
                || candidate.Status == Microsoft365DocumentWorkStatus.Processing
                    && candidate.LeaseExpiresAt <= now)
            .OrderBy(candidate => candidate.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (work is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        work.Status = Microsoft365DocumentWorkStatus.Processing;
        work.AttemptCount++;
        work.LeaseId = leaseId;
        work.LeaseExpiresAt = leaseExpiresAt;
        work.NextAttemptAt = null;
        work.LastErrorCode = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return work;
    }

    public Task CompleteAsync(
        Microsoft365DocumentWork work,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        work.Status = Microsoft365DocumentWorkStatus.Completed;
        work.CompletedAt = completedAt;
        work.LeaseId = null;
        work.LeaseExpiresAt = null;
        work.NextAttemptAt = null;
        work.LastErrorCode = null;
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task FailAsync(
        Microsoft365DocumentWork work,
        bool isPermanent,
        string errorCode,
        DateTimeOffset failedAt,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken = default)
    {
        work.Status = isPermanent
            ? Microsoft365DocumentWorkStatus.PermanentFailure
            : Microsoft365DocumentWorkStatus.TemporaryFailure;
        work.CompletedAt = isPermanent ? failedAt : null;
        work.LeaseId = null;
        work.LeaseExpiresAt = null;
        work.NextAttemptAt = isPermanent ? null : nextAttemptAt;
        work.LastErrorCode = errorCode;
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
