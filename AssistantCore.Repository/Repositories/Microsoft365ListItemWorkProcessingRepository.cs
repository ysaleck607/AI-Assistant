using System.Data;
using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Repository.Repositories;

public sealed class Microsoft365ListItemWorkProcessingRepository(AssistantCoreDbContext dbContext)
    : IMicrosoft365ListItemWorkProcessingRepository
{
    public async Task<Microsoft365ListItemWork?> ClaimNextAsync(
        Guid leaseId,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var work = await dbContext.Microsoft365ListItemWorks
            .Include(candidate => candidate.Organization)
            .Include(candidate => candidate.Microsoft365Source)
                .ThenInclude(source => source.Microsoft365Connection)
            .Where(candidate =>
                candidate.Status == Microsoft365ListItemWorkStatus.Pending
                || candidate.Status == Microsoft365ListItemWorkStatus.TemporaryFailure
                    && (candidate.NextAttemptAt == null || candidate.NextAttemptAt <= now)
                || candidate.Status == Microsoft365ListItemWorkStatus.Processing
                    && candidate.LeaseExpiresAt <= now)
            .OrderBy(candidate => candidate.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (work is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        work.Status = Microsoft365ListItemWorkStatus.Processing;
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
        Microsoft365ListItemWork work,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        work.Status = Microsoft365ListItemWorkStatus.Completed;
        work.CompletedAt = completedAt;
        work.LeaseId = null;
        work.LeaseExpiresAt = null;
        work.NextAttemptAt = null;
        work.LastErrorCode = null;
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task FailAsync(
        Microsoft365ListItemWork work,
        bool isPermanent,
        string errorCode,
        DateTimeOffset failedAt,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken = default)
    {
        work.Status = isPermanent
            ? Microsoft365ListItemWorkStatus.PermanentFailure
            : Microsoft365ListItemWorkStatus.TemporaryFailure;
        work.CompletedAt = isPermanent ? failedAt : null;
        work.LeaseId = null;
        work.LeaseExpiresAt = null;
        work.NextAttemptAt = isPermanent ? null : nextAttemptAt;
        work.LastErrorCode = errorCode;
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
