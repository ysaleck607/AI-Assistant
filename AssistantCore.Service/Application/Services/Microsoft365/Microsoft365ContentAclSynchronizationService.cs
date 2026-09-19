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
        ArgumentNullException.ThrowIfNull(acl);
        var content = await repository.FindAsync(
            organizationId,
            sourceId,
            externalContentId,
            cancellationToken);
        return content is null
            ? Microsoft365AclSynchronizationResult.NotRegistered
            : await SynchronizeAsync(content, acl, cancellationToken);
    }

    public async Task<Microsoft365AclSynchronizationResult> SynchronizeAsync(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        Microsoft365Acl acl,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(organizationId, sourceId, externalContentId);
        ArgumentNullException.ThrowIfNull(acl);
        var content = await repository.FindAsync(
            organizationId,
            sourceId,
            externalContentId,
            cancellationToken) ?? throw new InvalidOperationException(
                "The indexed content must be registered before its ACL is synchronized.");
        return await SynchronizeAsync(content, acl, cancellationToken);
    }

    private async Task<Microsoft365AclSynchronizationResult> SynchronizeAsync(
        Microsoft365IndexedContent content,
        Microsoft365Acl acl,
        CancellationToken cancellationToken)
    {
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

    private static string? NormalizeSiteUrl(string? siteUrl)
    {
        if (string.IsNullOrWhiteSpace(siteUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(siteUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The SharePoint site URL must use HTTPS.", nameof(siteUrl));
        }

        return uri.AbsoluteUri.TrimEnd('/');
    }

    private static string[] NormalizeChunkIds(IReadOnlyCollection<string> chunkIds)
    {
        ArgumentNullException.ThrowIfNull(chunkIds);
        if (chunkIds.Count == 0 || chunkIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one non-empty chunk identifier is required.", nameof(chunkIds));
        }

        return chunkIds
            .Distinct(StringComparer.Ordinal)
            .OrderBy(chunkId => chunkId, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateIdentity(
        Guid organizationId,
        Guid sourceId,
        string externalContentId)
    {
        if (organizationId == Guid.Empty || sourceId == Guid.Empty)
        {
            throw new ArgumentException("Organization and source identifiers are required.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(externalContentId);
    }

    private static void ValidateFingerprint(string aclFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aclFingerprint);
        if (aclFingerprint.Length != 64)
        {
            throw new ArgumentException("The ACL fingerprint must contain 64 characters.", nameof(aclFingerprint));
        }
    }

    private static void SynchronizePassages(
        Microsoft365IndexedContent content,
        IReadOnlyCollection<string> chunkIds)
    {
        var expectedChunkIds = chunkIds.ToHashSet(StringComparer.Ordinal);
        foreach (var obsoletePassage in content.Passages
                     .Where(passage => !expectedChunkIds.Contains(passage.ChunkId))
                     .ToArray())
        {
            content.Passages.Remove(obsoletePassage);
        }

        var existingChunkIds = content.Passages
            .Select(passage => passage.ChunkId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var chunkId in chunkIds.Where(chunkId => !existingChunkIds.Contains(chunkId)))
        {
            content.Passages.Add(new Microsoft365IndexedPassage
            {
                Id = Guid.NewGuid(),
                ChunkId = chunkId
            });
        }
    }
}
