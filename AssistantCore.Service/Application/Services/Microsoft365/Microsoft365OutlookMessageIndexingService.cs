using System.Security.Cryptography;
using System.Text;
using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Models.Microsoft365.Permissions;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public sealed class Microsoft365OutlookMessageIndexingService(
    IMicrosoft365SecurityIdentityNormalizer identityNormalizer,
    IMicrosoft365EmbeddingGenerator embeddingGenerator,
    IMicrosoft365DocumentChunkingService chunkingService,
    IMicrosoft365PassageIndexWriter indexWriter,
    IMicrosoft365ContentAclSynchronizationService aclSynchronizationService,
    IMicrosoft365IndexedContentRepository indexedContentRepository)
    : IMicrosoft365OutlookMessageIndexingService
{
    public async Task IndexAsync(
        Organization organization,
        Guid sourceId,
        string mailboxUserId,
        Microsoft365OutlookMessageDelta message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organization);
        ArgumentNullException.ThrowIfNull(message);
        if (organization.Id == Guid.Empty || sourceId == Guid.Empty)
        {
            throw new ArgumentException("Organization and source identifiers are required.");
        }

        if (message.IsDeleted || string.IsNullOrWhiteSpace(message.BodyContent))
        {
            await DeleteAsync(organization.Id, sourceId, message.Id, cancellationToken);
            return;
        }

        var normalizedMailboxUserId = identityNormalizer.NormalizeEntraUserId(mailboxUserId);
        var acl = new Microsoft365Acl(
            [normalizedMailboxUserId],
            [],
            [],
            hasAnonymousLink: false,
            hasOrganizationLink: false,
            Microsoft365AclInheritance.Unique);

        var content = message.BodyContent.Trim();
        var documentVersion = message.LastModifiedDateTime?.ToString("O")
            ?? message.ReceivedDateTime?.ToString("O")
            ?? "unknown";
        var passages = chunkingService.CreateTextChunks(
            organization.Id,
            sourceId,
            message.Id,
            documentVersion,
            string.IsNullOrWhiteSpace(message.Subject) ? "Courriel Outlook" : message.Subject.Trim(),
            message.WebLink,
            message.LastModifiedDateTime ?? message.ReceivedDateTime,
            content,
            "outlook");
        if (passages.Count == 0)
        {
            throw new InvalidDataException("Outlook message did not produce indexable passages.");
        }

        var embeddings = await embeddingGenerator.CreateAsync(
            passages.Select(passage => passage.Content).ToArray(),
            cancellationToken);
        if (embeddings.Count != passages.Count)
        {
            throw new InvalidOperationException("Outlook message embedding generation returned an unexpected result.");
        }

        var embeddedPassages = passages
            .Select((passage, index) => passage with { ContentVector = embeddings[index] })
            .ToArray();
        var existing = await indexedContentRepository.FindAsync(
            organization.Id,
            sourceId,
            message.Id,
            cancellationToken);

        await aclSynchronizationService.SynchronizeIfRegisteredAsync(
            organization.Id,
            sourceId,
            message.Id,
            acl,
            cancellationToken);
        await indexWriter.MergeOrUploadAsync(
            organization.Id,
            embeddedPassages,
            acl,
            cancellationToken);
        var currentChunkIds = embeddedPassages.Select(passage => passage.ChunkId).ToHashSet(StringComparer.Ordinal);
        var obsoleteChunkIds = existing?.Passages
            .Select(passage => passage.ChunkId)
            .Where(chunkId => !currentChunkIds.Contains(chunkId))
            .ToArray()
            ?? [];
        if (obsoleteChunkIds.Length > 0)
        {
            await indexWriter.DeleteAsync(obsoleteChunkIds, cancellationToken);
        }
        await aclSynchronizationService.RegisterAsync(
            organization.Id,
            sourceId,
            message.Id,
            embeddedPassages.Select(passage => passage.ChunkId).ToArray(),
            acl.Fingerprint,
            siteUrl: null,
            cancellationToken);
        await aclSynchronizationService.SynchronizeAsync(
            organization.Id,
            sourceId,
            message.Id,
            acl,
            cancellationToken);
    }

    public async Task DeleteAsync(
        Guid organizationId,
        Guid sourceId,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty || sourceId == Guid.Empty || string.IsNullOrWhiteSpace(messageId))
        {
            throw new ArgumentException("Organization, source and Outlook message identifiers are required.");
        }

        var existing = await indexedContentRepository.FindAsync(
            organizationId,
            sourceId,
            messageId,
            cancellationToken);
        var chunkIds = existing?.Passages
            .Select(passage => passage.ChunkId)
            .Where(chunkId => !string.IsNullOrWhiteSpace(chunkId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (chunkIds is null || chunkIds.Length == 0)
        {
            chunkIds = [CreateChunkId(sourceId, messageId)];
        }

        await indexWriter.DeleteAsync(chunkIds, cancellationToken);
        await aclSynchronizationService.MarkUnavailableIfRegisteredAsync(
            organizationId,
            sourceId,
            messageId,
            cancellationToken);
    }

    private static string CreateChunkId(Guid sourceId, string messageId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"outlook:{sourceId:N}:{messageId}")));
}
