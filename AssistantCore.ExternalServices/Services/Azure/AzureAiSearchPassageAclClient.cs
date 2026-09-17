using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssistantCore.ExternalServices.Entities.Azure;
using Azure.Core;
using Azure.Identity;

namespace AssistantCore.ExternalServices.Services.Azure;

public sealed class AzureAiSearchPassageAclClient
{
    private const int MaximumBatchSize = 1000;
    private const string ApiVersion = "2025-09-01";
    private static readonly string[] SearchScopes = ["https://search.azure.com/.default"];
    private readonly HttpClient httpClient;
    private readonly TokenCredential credential;

    public AzureAiSearchPassageAclClient(HttpClient httpClient)
        : this(httpClient, new DefaultAzureCredential())
    {
    }

    internal AzureAiSearchPassageAclClient(HttpClient httpClient, TokenCredential credential)
    {
        this.httpClient = httpClient;
        this.credential = credential;
    }

    public Task SetAvailabilityAsync(
        string endpoint,
        string indexName,
        string? apiKey,
        IReadOnlyCollection<string> chunkIds,
        bool isAvailable,
        CancellationToken cancellationToken = default) =>
        SendBatchesAsync(
            endpoint,
            indexName,
            apiKey,
            chunkIds.Select(chunkId => new Dictionary<string, object?>
            {
                ["@search.action"] = "merge",
                ["chunkId"] = chunkId,
                ["isAvailable"] = isAvailable
            }),
            cancellationToken);

    public Task MergeOrUploadAsync(
        string endpoint,
        string indexName,
        string? apiKey,
        IReadOnlyCollection<AzureAiSearchPassageDocument> passages,
        CancellationToken cancellationToken = default) =>
        SendBatchesAsync(
            endpoint,
            indexName,
            apiKey,
            passages.Select(passage => new Dictionary<string, object?>
            {
                ["@search.action"] = "mergeOrUpload",
                ["chunkId"] = passage.ChunkId,
                ["organizationId"] = passage.OrganizationId,
                ["sourceType"] = passage.SourceType,
                ["title"] = passage.Title,
                ["content"] = passage.Content,
                ["siteId"] = passage.SiteId,
                ["driveId"] = passage.DriveId,
                ["driveItemId"] = passage.DriveItemId,
                ["documentVersion"] = passage.DocumentVersion,
                ["chunkNumber"] = passage.ChunkNumber,
                ["url"] = passage.Url,
                ["archivePath"] = passage.ArchivePath,
                ["modifiedAt"] = passage.ModifiedAt,
                ["contentVector"] = passage.ContentVector,
                ["allowedUserIds"] = passage.AllowedUserIds,
                ["allowedGroupIds"] = passage.AllowedGroupIds,
                ["allowedSharePointGroupIds"] = passage.AllowedSharePointGroupIds,
                ["hasAnonymousLink"] = passage.HasAnonymousLink,
                ["hasOrganizationLink"] = passage.HasOrganizationLink,
                ["aclFingerprint"] = passage.AclFingerprint,
                ["isAvailable"] = passage.IsAvailable
            }),
            cancellationToken);

    public Task DeleteAsync(
        string endpoint,
        string indexName,
        string? apiKey,
        IReadOnlyCollection<string> chunkIds,
        CancellationToken cancellationToken = default) =>
        SendBatchesAsync(
            endpoint,
            indexName,
            apiKey,
            chunkIds.Select(chunkId => new Dictionary<string, object?>
            {
                ["@search.action"] = "delete",
                ["chunkId"] = chunkId
            }),
            cancellationToken);

    public Task UpdateAclAsync(
        string endpoint,
        string indexName,
        string? apiKey,
        IReadOnlyCollection<AzureAiSearchPassageAclUpdate> updates,
        CancellationToken cancellationToken = default) =>
        SendBatchesAsync(
            endpoint,
            indexName,
            apiKey,
            updates.Select(update => new Dictionary<string, object?>
            {
                ["@search.action"] = "merge",
                ["chunkId"] = update.ChunkId,
                ["allowedUserIds"] = update.AllowedUserIds,
                ["allowedGroupIds"] = update.AllowedGroupIds,
                ["allowedSharePointGroupIds"] = update.AllowedSharePointGroupIds,
                ["hasAnonymousLink"] = update.HasAnonymousLink,
                ["hasOrganizationLink"] = update.HasOrganizationLink,
                ["aclFingerprint"] = update.AclFingerprint,
                ["isAvailable"] = false
            }),
            cancellationToken);

    private async Task SendBatchesAsync(
        string endpoint,
        string indexName,
        string? apiKey,
        IEnumerable<Dictionary<string, object?>> actions,
        CancellationToken cancellationToken)
    {
        var requestUri = CreateRequestUri(endpoint, indexName);
        foreach (var batch in actions.Chunk(MaximumBatchSize))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = JsonContent.Create(new { value = batch })
            };
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                var token = await credential.GetTokenAsync(
                    new TokenRequestContext(SearchScopes),
                    cancellationToken);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            }
            else
            {
                request.Headers.TryAddWithoutValidation("api-key", apiKey);
            }
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new AzureAiSearchExternalException(
                    $"Azure AI Search rejected an indexing batch with status {(int)response.StatusCode}.");
            }

            var result = await response.Content.ReadFromJsonAsync<IndexDocumentsResponse>(
                cancellationToken: cancellationToken);
            if (result?.Value is null
                || result.Value.Count != batch.Length
                || result.Value.Any(item => !item.Status))
            {
                throw new AzureAiSearchExternalException(
                    "Azure AI Search did not apply every operation in the indexing batch.");
            }
        }
    }

    private static Uri CreateRequestUri(string endpoint, string indexName)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
            || endpointUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("An HTTPS Azure AI Search endpoint is required.", nameof(endpoint));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);
        return new Uri(
            endpointUri,
            $"/indexes('{Uri.EscapeDataString(indexName)}')/docs/search.index?api-version={ApiVersion}");
    }

    private sealed record IndexDocumentsResponse(
        [property: JsonPropertyName("value")] IReadOnlyCollection<IndexDocumentResult> Value);

    private sealed record IndexDocumentResult(
        [property: JsonPropertyName("status")] bool Status);
}
