using AssistantCore.ExternalServices.Entities.Azure;
using AssistantCore.ExternalServices.Services.Azure;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Models.Microsoft365.Permissions;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Infrastructure.Microsoft365;

public sealed class Microsoft365PassageIndexWriterAdapter(
    AzureAiSearchPassageAclClient client,
    IOptions<AzureAiSearchOptions> options) : IMicrosoft365PassageIndexWriter
{
    public Task MergeOrUploadAsync(
        Guid organizationId,
        IReadOnlyCollection<Microsoft365SearchPassage> passages,
        Microsoft365Acl acl,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(passages);
        ArgumentNullException.ThrowIfNull(acl);
        var configuration = options.Value;
        if (string.IsNullOrWhiteSpace(configuration.Endpoint)
            || string.IsNullOrWhiteSpace(configuration.IndexName))
        {
            throw new InvalidOperationException(
                "AzureSearch endpoint and index name are required for passage indexing.");
        }

        var documents = passages.Select(passage => new AzureAiSearchPassageDocument(
            passage.ChunkId,
            organizationId.ToString("D"),
            passage.Title,
            passage.Content,
            acl.AllowedEntraUserIds,
            acl.AllowedEntraGroupIds,
            acl.AllowedSharePointGroupIds,
            acl.HasAnonymousLink,
            acl.HasOrganizationLink,
            acl.Fingerprint,
            IsAvailable: false,
            passage.SiteId,
            passage.DriveId,
            passage.DriveItemId,
            passage.DocumentVersion,
            passage.ChunkNumber,
            passage.Url,
            passage.ModifiedAt,
            passage.ContentVector,
            passage.SourceType,
            passage.ArchivePath)).ToArray();
        return client.MergeOrUploadAsync(
            configuration.Endpoint,
            configuration.IndexName,
            configuration.ApiKey,
            documents,
            cancellationToken);
    }

    public Task DeleteAsync(
        IReadOnlyCollection<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        var configuration = options.Value;
        return client.DeleteAsync(
            configuration.Endpoint,
            configuration.IndexName,
            configuration.ApiKey,
            chunkIds,
            cancellationToken);
    }
}
