using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Repository.Repositories;

public sealed class Microsoft365IndexedContentRepository(AssistantCoreDbContext dbContext)
    : IMicrosoft365IndexedContentRepository
{
    public Task<Microsoft365IndexedContent?> FindAsync(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        CancellationToken cancellationToken = default) =>
        dbContext.Microsoft365IndexedContents
            .Include(content => content.Passages)
            .SingleOrDefaultAsync(content =>
                content.OrganizationId == organizationId
                && content.Microsoft365SourceId == sourceId
                && content.ExternalContentId == externalContentId,
                cancellationToken);

    public async Task<IReadOnlyCollection<Microsoft365IndexedContent>> FindAvailableByTitleAsync(
        Guid organizationId,
        string title,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(organizationId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        // Title is encrypted at rest with a non-deterministic cipher, so an exact match cannot
        // be pushed down to SQL (the same plaintext never produces the same ciphertext twice).
        // The organization/availability filters keep the candidate set scoped to one tenant
        // before the title comparison happens in memory, after decryption.
        var candidates = await dbContext.Microsoft365IndexedContents
            .AsNoTracking()
            .Include(content => content.Microsoft365Source)
            .Include(content => content.Passages)
            .Where(content => content.OrganizationId == organizationId && content.IsAvailable)
            .ToArrayAsync(cancellationToken);

        return candidates
            .Where(content => content.Title == title)
            .ToArray();
    }

    public async Task<IReadOnlyCollection<Microsoft365IndexedContent>> GetAclReconciliationCandidatesAsync(
        DateTimeOffset dueAt,
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        if (maximumResults <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumResults));
        }

        return await dbContext.Microsoft365IndexedContents
            .AsNoTracking()
            .Include(content => content.Organization)
            .Include(content => content.Microsoft365Source)
            .Include(content => content.Passages)
            .Where(content =>
                content.NextAclReconciliationAt == null
                || content.NextAclReconciliationAt <= dueAt)
            .OrderBy(content => content.NextAclReconciliationAt)
            .ThenBy(content => content.UpdatedAt)
            .Take(maximumResults)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<Microsoft365IndexedContent>> GetBySourceAsync(
        Guid organizationId,
        Guid sourceId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Microsoft365IndexedContents
            .Include(content => content.Passages)
            .Where(content =>
                content.OrganizationId == organizationId
                && content.Microsoft365SourceId == sourceId)
            .ToArrayAsync(cancellationToken);

    public async Task RequestAclReconciliationAsync(
        Guid sourceId,
        DateTimeOffset dueAt,
        CancellationToken cancellationToken = default)
    {
        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("The Microsoft 365 source identifier is required.", nameof(sourceId));
        }

        await dbContext.Microsoft365IndexedContents
            .Where(content =>
                content.Microsoft365SourceId == sourceId
                && (content.NextAclReconciliationAt == null
                    || content.NextAclReconciliationAt > dueAt))
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(
                    content => content.NextAclReconciliationAt,
                    dueAt),
                cancellationToken);
    }

    public async Task SaveAsync(
        Microsoft365IndexedContent content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (dbContext.Entry(content).State == EntityState.Detached)
        {
            dbContext.Microsoft365IndexedContents.Add(content);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        Microsoft365IndexedContent content,
        CancellationToken cancellationToken = default)
    {
        dbContext.Microsoft365IndexedContents.Remove(content);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
