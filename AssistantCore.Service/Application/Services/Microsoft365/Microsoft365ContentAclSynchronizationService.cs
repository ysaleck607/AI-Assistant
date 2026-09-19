using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Models.Microsoft365.Permissions;
using AssistantCore.Service.Application.Configuration;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public sealed class Microsoft365ContentAclSynchronizationService(
    IMicrosoft365IndexedContentRepository repository,
    IMicrosoft365PassageAclWriter passageWriter,
    IOptions<Microsoft365Options> options,
    TimeProvider timeProvider) : IMicrosoft365ContentAclSynchronizationService
{
    public async Task RegisterAsync(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        IReadOnlyCollection<string> chunkIds,
        string aclFingerprint,
        string? siteUrl,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(organizationId, sourceId, externalContentId);
        var normalizedChunkIds = NormalizeChunkIds(chunkIds);
        ValidateFingerprint(aclFingerprint);

        var now = timeProvider.GetUtcNow();
        var content = await repository.FindAsync(
            organizationId,
            sourceId,
            externalContentId,
            cancellationToken);
        if (content is null)
        {
            content = new Microsoft365IndexedContent
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                Microsoft365SourceId = sourceId,
                ExternalContentId = externalContentId,
                CreatedAt = now
            };
        }

        SynchronizePassages(content, normalizedChunkIds);
        content.SiteUrl = NormalizeSiteUrl(siteUrl);
        content.AclFingerprint = aclFingerprint;
        content.IsAvailable = false;
        content.NextAclReconciliationAt = null;
        content.UpdatedAt = now;
        await repository.SaveAsync(content, cancellationToken);
    }

    public async Task<bool> MarkUnavailableIfRegisteredAsync(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(organizationId, sourceId, externalContentId);
        var content = await repository.FindAsync(
            organizationId,
            sourceId,
            externalContentId,
            cancellationToken);
        if (content is null)
        {
            return false;
        }

        var chunkIds = NormalizeChunkIds(content.Passages.Select(passage => passage.ChunkId).ToArray());
        if (content.IsAvailable)
        {
            await passageWriter.SetAvailabilityAsync(chunkIds, false, cancellationToken);
        }

        content.IsAvailable = false;
        var now = timeProvider.GetUtcNow();
        content.NextAclReconciliationAt = now.AddMinutes(
            options.Value.AclReconciliationRetryMinutes);
        content.UpdatedAt = now;
        await repository.SaveAsync(content, cancellationToken);
        return true;
    }

    public async Task<Microsoft365AclSynchronizationResult> SynchronizeIfRegisteredAsync(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        Microsoft365Acl acl,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(organizationId, sourceId, externalContentId);
        var content = await repository.FindAsync(
            organizationId,
            sourceId,
            externalContentId,
            cancellationToken);
        if (content is null)
        {
            return Microsoft365AclSynchronizationResult.NotRegistered;
        }

        return await SynchronizeRegisteredContentAsync(content, acl, cancellationToken);
    }

    public async Task<Microsoft365AclSynchronizationResult> SynchronizeAsync(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        Microsoft365Acl acl,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(organizationId, sourceId, externalContentId);
        var content = await repository.FindAsync(
            organizationId,
            sourceId,
            externalContentId,
            cancellationToken)
            ?? throw new InvalidOperationException("Indexed Microsoft 365 content was not registered before ACL synchronization.");

        return await SynchronizeRegisteredContentAsync(content, acl, cancellationToken);
    }

    private async Task<Microsoft365AclSynchronizationResult> SynchronizeRegisteredContentAsync(
        Microsoft365IndexedContent content,
        Microsoft365Acl acl,
        CancellationToken cancellationToken)
    {
        ValidateFingerprint(acl.Fingerprint);
        var chunkIds = NormalizeChunkIds(content.Passages.Select(passage => passage.ChunkId).ToArray());
        if (string.Equals(content.AclFingerprint, acl.Fingerprint, StringComparison.Ordinal))
        {
            if (content.IsAvailable)
            {
                ScheduleNextReconciliation(content);
                await repository.SaveAsync(content, cancellationToken);
                return Microsoft365AclSynchronizationResult.Unchanged;
            }

            await PublishAsync(content, chunkIds, cancellationToken);
            return Microsoft365AclSynchronizationResult.Published;
        }

        await passageWriter.SetAvailabilityAsync(chunkIds, false, cancellationToken);
        content.IsAvailable = false;
        content.UpdatedAt = timeProvider.GetUtcNow();
        await repository.SaveAsync(content, cancellationToken);

        await passageWriter.UpdateAclAsync(chunkIds, acl, cancellationToken);
        content.AclFingerprint = acl.Fingerprint;
        content.UpdatedAt = timeProvider.GetUtcNow();
        await repository.SaveAsync(content, cancellationToken);

        await PublishAsync(content, chunkIds, cancellationToken);
        return Microsoft365AclSynchronizationResult.Updated;
    }

    private async Task PublishAsync(
        Microsoft365IndexedContent content,
        IReadOnlyCollection<string> chunkIds,
        CancellationToken cancellationToken)
    {
        await passageWriter.SetAvailabilityAsync(chunkIds, true, cancellationToken);
        content.IsAvailable = true;
        ScheduleNextReconciliation(content);
        await repository.SaveAsync(content, cancellationToken);
    }

    private void ScheduleNextReconciliation(Microsoft365IndexedContent content)
    {
        var now = timeProvider.GetUtcNow();
        content.NextAclReconciliationAt = now.AddMinutes(
            options.Value.AclReconciliationIntervalMinutes);
        content.UpdatedAt = now;
    }

    private static void ValidateIdentity(Guid organizationId, Guid sourceId, string externalContentId)
    {
        if (organizationId == Guid.Empty) throw new ArgumentException("Organization id is required.", nameof(organizationId));
        if (sourceId == Guid.Empty) throw new ArgumentException("Source id is required.", nameof(sourceId));
        if (string.IsNullOrWhiteSpace(externalContentId)) throw new ArgumentException("External content id is required.", nameof(externalContentId));
    }

    private static void ValidateFingerprint(string aclFingerprint)
    {
        if (string.IsNullOrWhiteSpace(aclFingerprint))
        {
            throw new ArgumentException("ACL fingerprint is required.", nameof(aclFingerprint));
        }
    }

    private static IReadOnlyCollection<string> NormalizeChunkIds(IReadOnlyCollection<string> chunkIds)
    {
        ArgumentNullException.ThrowIfNull(chunkIds);
        var normalized = chunkIds
            .Where(chunkId => !string.IsNullOrWhiteSpace(chunkId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("At least one chunk id is required.", nameof(chunkIds));
        }

        return normalized;
    }

    private static string? NormalizeSiteUrl(string? siteUrl) =>
        string.IsNullOrWhiteSpace(siteUrl) ? null : siteUrl.Trim();

    private static void SynchronizePassages(
        Microsoft365IndexedContent content,
        IReadOnlyCollection<string> chunkIds)
    {
        var expectedChunkIds = chunkIds.ToHashSet(StringComparer.Ordinal);
        var removed = content.Passages
            .Where(passage => !expectedChunkIds.Contains(passage.ChunkId))
            .ToArray();
        foreach (var passage in removed)
        {
            content.Passages.Remove(passage);
        }

        var knownChunkIds = content.Passages
            .Select(passage => passage.ChunkId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var chunkId in chunkIds)
        {
            if (knownChunkIds.Contains(chunkId))
            {
                continue;
            }

            content.Passages.Add(new Microsoft365IndexedPassage
            {
                Id = Guid.NewGuid(),
                Microsoft365IndexedContentId = content.Id,
                ChunkId = chunkId
            });
        }
    }
}
