using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Repository.Repositories;

public sealed class Microsoft365ListSynchronizationRepository(AssistantCoreDbContext dbContext)
    : IMicrosoft365ListSynchronizationRepository
{
    public Task<Microsoft365List?> FindForSynchronizationAsync(
        Guid sourceId,
        CancellationToken cancellationToken = default) =>
        dbContext.Microsoft365Lists
            .AsNoTracking()
            .Include(list => list.Microsoft365Connection)
                .ThenInclude(connection => connection.OrganizationConnector)
            .SingleOrDefaultAsync(list => list.Id == sourceId, cancellationToken);

    public async Task<bool> SaveSchemaAsync(
        Guid sourceId,
        Guid leaseId,
        string schemaFingerprint,
        bool requiresItemReprocessing,
        CancellationToken cancellationToken = default)
    {
        var affectedRows = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE list
            SET [SchemaFingerprint] = {schemaFingerprint},
                [RequiresItemReprocessing] = {requiresItemReprocessing}
            FROM [dbo].[Microsoft365List] list
            INNER JOIN [dbo].[Microsoft365Source] source ON source.[Id] = list.[Id]
            WHERE list.[Id] = {sourceId}
              AND source.[SynchronizationLeaseId] = {leaseId};
            """, cancellationToken);

        return affectedRows == 1;
    }

    public async Task<int> SaveWorkPageAsync(
        Guid sourceId,
        Guid synchronizationId,
        Guid leaseId,
        DateTimeOffset leaseExpiresAt,
        IReadOnlyCollection<Microsoft365ListItemWorkData> works,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var renewedLeaseRows = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [dbo].[Microsoft365Source]
            SET [SynchronizationLeaseExpiresAt] = {leaseExpiresAt}
            WHERE [Id] = {sourceId}
              AND [SynchronizationLeaseId] = {leaseId};
            """, cancellationToken);
        if (renewedLeaseRows != 1)
        {
            throw new InvalidOperationException(
                "The Microsoft 365 source synchronization lease was lost.");
        }

        var synchronizationExists = await dbContext.Microsoft365Synchronizations
            .AsNoTracking()
            .AnyAsync(synchronization =>
                synchronization.Id == synchronizationId
                && synchronization.Microsoft365SourceId == sourceId,
                cancellationToken);
        if (!synchronizationExists)
        {
            throw new InvalidOperationException(
                "The Microsoft 365 synchronization does not belong to the requested source.");
        }

        var distinctWorks = works
            .GroupBy(work => work.DeduplicationKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var deduplicationKeys = distinctWorks
            .Select(work => work.DeduplicationKey)
            .ToArray();
        var existingKeys = deduplicationKeys.Length == 0
            ? Array.Empty<string>()
            : await dbContext.Microsoft365ListItemWorks
                .AsNoTracking()
                .Where(work => deduplicationKeys.Contains(work.DeduplicationKey))
                .Select(work => work.DeduplicationKey)
                .ToArrayAsync(cancellationToken);
        var existingKeySet = existingKeys.ToHashSet(StringComparer.Ordinal);
        var newWorks = distinctWorks
            .Where(work => !existingKeySet.Contains(work.DeduplicationKey))
            .Select(work => new Microsoft365ListItemWork
            {
                Id = Guid.NewGuid(),
                OrganizationId = work.OrganizationId,
                Microsoft365SourceId = sourceId,
                Microsoft365SynchronizationId = synchronizationId,
                WorkType = work.WorkType,
                SiteId = work.SiteId,
                ListId = work.ListId,
                ListItemId = work.ListItemId,
                ETag = work.ETag,
                CreatedDateTime = work.CreatedDateTime,
                LastModifiedDateTime = work.LastModifiedDateTime,
                WebUrl = work.WebUrl,
                FieldsJson = work.FieldsJson,
                DeduplicationKey = work.DeduplicationKey,
                Status = Microsoft365ListItemWorkStatus.Pending,
                CreatedAt = work.CreatedAt
            })
            .ToArray();

        dbContext.Microsoft365ListItemWorks.AddRange(newWorks);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return newWorks.Length;
    }
}
