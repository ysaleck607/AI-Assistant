using System.Data;
using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Repository.Repositories;

public sealed class Microsoft365PendingSynchronizationRepository(AssistantCoreDbContext dbContext)
    : IMicrosoft365PendingSynchronizationRepository
{
    public async Task<Microsoft365PendingSynchronization?> ClaimNextAsync(
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var synchronization = await dbContext.Microsoft365Synchronizations
            .Include(candidate => candidate.Microsoft365Source)
                .ThenInclude(source => source.Microsoft365Connection)
            .Where(candidate =>
                (candidate.Microsoft365Source is Microsoft365Drive
                    || candidate.Microsoft365Source is Microsoft365List
                    || candidate.Microsoft365Source.Kind == Microsoft365SourceKind.OutlookMailbox)
                && (candidate.Microsoft365Source.SynchronizationLeaseId == null
                    || candidate.Microsoft365Source.SynchronizationLeaseExpiresAt <= startedAt)
                && (candidate.Status == Microsoft365SynchronizationStatus.Pending
                    || candidate.Status == Microsoft365SynchronizationStatus.TemporaryFailure
                    || candidate.Status == Microsoft365SynchronizationStatus.Running)
                && (candidate.Type == Microsoft365SynchronizationType.Initial
                    || candidate.Type == Microsoft365SynchronizationType.Delta
                    || candidate.Type == Microsoft365SynchronizationType.IndexCleanup)
                && !candidate.Microsoft365Source.Synchronizations.Any(other =>
                    other.Id != candidate.Id
                    && other.Status == Microsoft365SynchronizationStatus.Running))
            .OrderBy(candidate => candidate.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (synchronization is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        synchronization.Status = Microsoft365SynchronizationStatus.Running;
        synchronization.StartedAt = startedAt;
        synchronization.AttemptCount++;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var organizationId = synchronization.Microsoft365Source.Microsoft365Connection.OrganizationId;

        return new Microsoft365PendingSynchronization(
            synchronization.Id,
            synchronization.Microsoft365SourceId,
            organizationId,
            synchronization.Microsoft365Source.Kind,
            synchronization.Microsoft365Source.Status,
            synchronization.Microsoft365Source.IsIndexed,
            synchronization.Type,
            synchronization.Microsoft365ReindexOperationId);
    }
}
