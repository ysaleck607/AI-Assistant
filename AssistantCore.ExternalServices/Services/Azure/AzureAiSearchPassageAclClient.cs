using System.Net;
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
    private const int MaximumAttempts = 3;
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
        Guid organizationId,
        IReadOnlyCollection<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("An organization identifier is required.", nameof(organizationId));
        }

        ArgumentNullException.ThrowIfNull(chunkIds);
        return DeleteForOrganizationAsync(
            endpoint,
            indexName,
            apiKey,
            organizationId,
            chunkIds,
            cancellationToken);
    }

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
            using var response = await SendWithRetryAsync(
                () => new HttpRequestMessage(HttpMethod.Post, requestUri)
                {
                    Content = JsonContent.Create(new { value = batch })
                },
                apiKey,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new AzureAiSearchExternalException(
                    $"Azure AI Search rejected an indexing batch with status {(int)response.StatusCode}: {responseBody}");
            }

            var result = await response.Content.ReadFromJsonAsync<IndexDocumentsResponse>(
                cancellationToken: cancellationToken);
            if (result?.Value is null
                || result.Value.Count != batch.Length
                || result.Value.Any(item => !item.Status))
            {
                var failures = result?.Value?
                    .Where(item => !item.Status)
                    .Select(item =>
                        $"key '{item.Key ?? "unknown"}' returned status {item.StatusCode?.ToString() ?? "unknown"}"
                            + (string.IsNullOrWhiteSpace(item.ErrorMessage)
                                ? string.Empty
                                : $": {item.ErrorMessage}"))
                    .ToArray();
                var details = failures is { Length: > 0 }
                    ? $" Details: {string.Join("; ", failures)}"
                    : $" Expected {batch.Length} results but received {result?.Value?.Count ?? 0}.";
                throw new AzureAiSearchExternalException(
                    $"Azure AI Search did not apply every operation in the indexing batch.{details}");
            }
        }
    }

    private async Task DeleteForOrganizationAsync(
        string endpoint,
        string indexName,
        string? apiKey,
        Guid organizationId,
        IReadOnlyCollection<string> requestedChunkIds,
        CancellationToken cancellationToken)
    {
        foreach (var chunkBatch in requestedChunkIds.Chunk(MaximumBatchSize))
        {
            var indexedChunkIds = await FindChunkIdsAsync(
                endpoint,
                indexName,
                apiKey,
                organizationId,
                chunkBatch,
                cancellationToken);
            if (indexedChunkIds.Count == 0)
            {
                continue;
            }

            await SendBatchesAsync(
                endpoint,
                indexName,
                apiKey,
                indexedChunkIds.Select(chunkId => new Dictionary<string, object?>
                {
                    ["@search.action"] = "delete",
                    ["chunkId"] = chunkId
                }),
                cancellationToken);
        }
    }

    private async Task<IReadOnlyCollection<string>> FindChunkIdsAsync(
        string endpoint,
        string indexName,
        string? apiKey,
        Guid organizationId,
        IReadOnlyCollection<string> chunkIds,
        CancellationToken cancellationToken)
    {
        var requestUri = CreateSearchRequestUri(endpoint, indexName);
        var filter = $"organizationId eq '{organizationId:D}' and search.in(chunkId, '{string.Join("','", chunkIds.Select(EscapeODataString))}')";
        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = JsonContent.Create(new
                {
                    search = "*",
                    filter,
                    select = "chunkId",
                    top = MaximumBatchSize
                })
            },
            apiKey,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new AzureAiSearchExternalException(
                $"Azure AI Search rejected the organization-scoped deletion query with status {(int)response.StatusCode}: {errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<SearchResponse>(
            cancellationToken: cancellationToken);
        return result?.Value?
                   .Select(item => item.ChunkId)
                   .Where(chunkId => !string.IsNullOrWhiteSpace(chunkId))
                   .ToArray()
               ?? [];
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = requestFactory();
            await AuthorizeAsync(request, apiKey, cancellationToken);
            var response = await httpClient.SendAsync(request, cancellationToken);
            if (!IsTransient(response.StatusCode) || attempt >= MaximumAttempts)
            {
                return response;
            }

            var delay = response.Headers.RetryAfter?.Delta
                        ?? TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout
        || statusCode == (HttpStatusCode)429
        || (int)statusCode >= 500;

    private async Task AuthorizeAsync(
        HttpRequestMessage request,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.TryAddWithoutValidation("api-key", apiKey);
            return;
        }

        var token = await credential.GetTokenAsync(
            new TokenRequestContext(SearchScopes),
            cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
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

    private static Uri CreateSearchRequestUri(string endpoint, string indexName) =>
        new(
            CreateRequestUri(endpoint, indexName).ToString()
                .Replace("/docs/search.index", "/docs/search", StringComparison.Ordinal));

    private static string EscapeODataString(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    private sealed record IndexDocumentsResponse(
        [property: JsonPropertyName("value")] IReadOnlyCollection<IndexDocumentResult> Value);

    private sealed record IndexDocumentResult(
        [property: JsonPropertyName("key")] string? Key,
        [property: JsonPropertyName("status")] bool Status,
        [property: JsonPropertyName("statusCode")] int? StatusCode,
        [property: JsonPropertyName("errorMessage")] string? ErrorMessage);

    private sealed record SearchResponse(
        [property: JsonPropertyName("value")] IReadOnlyCollection<SearchDocument> Value);

    private sealed record SearchDocument(
        [property: JsonPropertyName("chunkId")] string ChunkId);
}
