using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AssistantCore.ExternalServices.Entities.Azure;
using Azure.Core;
using Azure.Identity;

namespace AssistantCore.ExternalServices.Services.Azure;

public sealed class AzureAiSearchKnowledgeBaseRetrievalClient
{
    private const string ApiVersion = "2026-08-01-preview";
    private static readonly string[] SearchScopes = ["https://search.azure.com/.default"];
    private readonly HttpClient httpClient;
    private readonly TokenCredential credential;

    public AzureAiSearchKnowledgeBaseRetrievalClient(HttpClient httpClient)
        : this(httpClient, new DefaultAzureCredential())
    {
    }

    internal AzureAiSearchKnowledgeBaseRetrievalClient(
        HttpClient httpClient,
        TokenCredential credential)
    {
        this.httpClient = httpClient;
        this.credential = credential;
    }

    public async Task<AzureAiSearchKnowledgeBaseRetrievalResult> RetrieveAsync(
        string endpoint,
        string? apiKey,
        AzureAiSearchKnowledgeBaseRetrievalRequest retrievalRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentNullException.ThrowIfNull(retrievalRequest);
        ValidateRequest(retrievalRequest);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            CreateRetrieveUri(endpoint, retrievalRequest.KnowledgeBaseName))
        {
            Content = JsonContent.Create(CreatePayload(retrievalRequest))
        };
        await AuthorizeAsync(request, apiKey, cancellationToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new AzureAiSearchExternalException(
                $"Azure AI Search rejected a knowledge base retrieval with status {(int)response.StatusCode}: {errorBody}");
        }

        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

            return MapResult(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new AzureAiSearchExternalException(
                $"Azure AI Search returned an invalid knowledge base retrieval response: {exception.Message}");
        }
    }

    private static object CreatePayload(
        AzureAiSearchKnowledgeBaseRetrievalRequest request)
    {
        var retrievalCandidateLimit = Math.Clamp(request.RetrievalCandidateLimit, 50, 200);
        var finalEvidenceLimit = Math.Clamp(request.FinalEvidenceLimit, 1, 50);
        var reasoningEffort = NormalizeReasoningEffort(request.RetrievalReasoningEffort);

        var payload = new Dictionary<string, object?>
        {
            ["outputMode"] = "extractiveData",
            ["retrievalReasoningEffort"] = new { kind = reasoningEffort },
            ["includeActivity"] = true,
            ["maxRuntimeInSeconds"] = request.MaxRuntimeInSeconds,
            ["maxOutputSize"] = request.MaxOutputSizeInTokens,
            ["maxOutputDocuments"] = finalEvidenceLimit,
            ["knowledgeSourceParams"] = new[]
            {
                new
                {
                    knowledgeSourceName = request.KnowledgeSourceName,
                    kind = "searchIndex",
                    alwaysQuerySource = true,
                    includeReferences = true,
                    includeReferenceSourceData = true,
                    filterAddOn = request.Filter,
                    maxOutputDocuments = retrievalCandidateLimit
                }
            }
        };

        if (string.Equals(reasoningEffort, "minimal", StringComparison.Ordinal))
        {
            payload["intents"] = new[]
            {
                new
                {
                    type = "semantic",
                    search = request.Query
                }
            };
        }
        else
        {
            payload["messages"] = request.ConversationHistory
                .Select(message => new
                {
                    role = message.Role,
                    content = new[]
                    {
                        new
                        {
                            type = "text",
                            text = message.Content
                        }
                    }
                })
                .Append(new
                {
                    role = "user",
                    content = new[]
                    {
                        new
                        {
                            type = "text",
                            text = request.Query
                        }
                    }
                })
                .ToArray();
        }

        return payload;
    }

    private static string NormalizeReasoningEffort(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "minimal" or "low" or "auto"
            ? normalized
            : throw new ArgumentException(
                "Azure AI Search retrieval reasoning effort must be 'minimal', 'low' or 'auto'.",
                nameof(value));
    }

    private static AzureAiSearchKnowledgeBaseRetrievalResult MapResult(
        JsonElement root)
    {
        var mergedContent = ReadMergedContent(root);
        var references = ReadReferences(root, mergedContent);
        var activity = ReadActivity(root);

        return new AzureAiSearchKnowledgeBaseRetrievalResult(
            mergedContent,
            references,
            activity);
    }

    private static string ReadMergedContent(JsonElement root)
    {
        if (!root.TryGetProperty("response", out var response)
            || response.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var message in response.EnumerateArray())
        {
            if (!message.TryGetProperty("content", out var contents)
                || contents.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var content in contents.EnumerateArray())
            {
                if (TryGetString(content, "text") is { } text)
                {
                    return text;
                }
            }
        }

        return string.Empty;
    }

    private static IReadOnlyCollection<AzureAiSearchKnowledgeBaseReference> ReadReferences(
        JsonElement root,
        string mergedContent)
    {
        var references = new List<AzureAiSearchKnowledgeBaseReference>();
        if (root.TryGetProperty("references", out var referenceElements)
            && referenceElements.ValueKind == JsonValueKind.Array)
        {
            references.AddRange(referenceElements
                .EnumerateArray()
                .Select(MapReference)
                .OfType<AzureAiSearchKnowledgeBaseReference>());
        }

        foreach (var extractedReference in ParseExtractedReferences(mergedContent))
        {
            var existingIndex = references.FindIndex(reference => string.Equals(
                    reference.ReferenceId,
                    extractedReference.ReferenceId,
                    StringComparison.Ordinal));
            if (existingIndex < 0)
            {
                references.Add(extractedReference);
                continue;
            }

            if (HasDocumentSecurityContext(extractedReference)
                && !HasDocumentSecurityContext(references[existingIndex]))
            {
                references[existingIndex] = extractedReference;
            }
        }

        return references;
    }

    private static bool HasDocumentSecurityContext(
        AzureAiSearchKnowledgeBaseReference reference) =>
        !string.IsNullOrWhiteSpace(reference.SiteId)
        && !string.IsNullOrWhiteSpace(reference.DriveId)
        && !string.IsNullOrWhiteSpace(reference.DriveItemId);

    private static AzureAiSearchKnowledgeBaseReference? MapReference(JsonElement reference)
    {
        var sourceData = reference.TryGetProperty("sourceData", out var source)
            && source.ValueKind == JsonValueKind.Object
            ? source
            : reference;
        var referenceId = TryGetString(reference, "id")
            ?? TryGetString(reference, "referenceId")
            ?? TryGetString(reference, "ref_id");
        var documentKey = TryGetString(sourceData, "chunkId")
            ?? TryGetString(sourceData, "key")
            ?? TryGetString(sourceData, "id")
            ?? TryGetString(sourceData, "docKey")
            ?? referenceId;
        var title = TryGetString(sourceData, "title");
        var content = TryGetString(sourceData, "content");

        if (string.IsNullOrWhiteSpace(referenceId)
            || string.IsNullOrWhiteSpace(documentKey)
            || string.IsNullOrWhiteSpace(title)
            || string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        return new AzureAiSearchKnowledgeBaseReference(
            referenceId,
            documentKey,
            title,
            content,
            TryGetString(sourceData, "siteId"),
            TryGetString(sourceData, "driveId"),
            TryGetString(sourceData, "driveItemId"),
            TryGetString(sourceData, "url"),
            TryGetDateTimeOffset(sourceData, "modifiedAt"),
            TryGetDouble(reference, "rerankerScore") ?? TryGetDouble(reference, "score"),
            TryGetString(sourceData, "archivePath"));
    }

    private static IReadOnlyCollection<AzureAiSearchKnowledgeBaseReference> ParseExtractedReferences(
        string mergedContent)
    {
        if (string.IsNullOrWhiteSpace(mergedContent))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(mergedContent);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return document.RootElement
                .EnumerateArray()
                .Select(MapExtractedReference)
                .OfType<AzureAiSearchKnowledgeBaseReference>()
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static AzureAiSearchKnowledgeBaseReference? MapExtractedReference(
        JsonElement reference)
    {
        var referenceId = TryGetString(reference, "ref_id")
            ?? TryGetString(reference, "id");
        var documentKey = TryGetString(reference, "chunkId")
            ?? TryGetString(reference, "key")
            ?? TryGetString(reference, "docKey")
            ?? referenceId;
        var title = TryGetString(reference, "title");
        var content = TryGetString(reference, "content");

        if (string.IsNullOrWhiteSpace(referenceId)
            || string.IsNullOrWhiteSpace(documentKey)
            || string.IsNullOrWhiteSpace(title)
            || string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        return new AzureAiSearchKnowledgeBaseReference(
            referenceId,
            documentKey,
            title,
            content,
            TryGetString(reference, "siteId"),
            TryGetString(reference, "driveId"),
            TryGetString(reference, "driveItemId"),
            TryGetString(reference, "url"),
            TryGetDateTimeOffset(reference, "modifiedAt"),
            TryGetDouble(reference, "rerankerScore") ?? TryGetDouble(reference, "score"),
            TryGetString(reference, "archivePath"));
    }

    private static IReadOnlyCollection<AzureAiSearchKnowledgeBaseActivity> ReadActivity(
        JsonElement root)
    {
        if (!root.TryGetProperty("activity", out var activities)
            || activities.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return activities
            .EnumerateArray()
            .Select(activity =>
            {
                var arguments = activity.TryGetProperty("searchIndexArguments", out var searchIndexArguments)
                    ? searchIndexArguments
                    : default;

                return new AzureAiSearchKnowledgeBaseActivity(
                    TryGetString(activity, "type") ?? string.Empty,
                    TryGetString(activity, "knowledgeSourceName"),
                    arguments.ValueKind == JsonValueKind.Object
                        ? TryGetString(arguments, "search")
                        : null,
                    TryGetInt32(activity, "count"),
                    TryGetInt32(activity, "elapsedMs"));
            })
            .Where(activity => !string.IsNullOrWhiteSpace(activity.Type))
            .ToArray();
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static double? TryGetDouble(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return property.TryGetDouble(out var value) ? value : null;
    }

    private static int? TryGetInt32(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return property.TryGetInt32(out var value) ? value : null;
    }

    private static DateTimeOffset? TryGetDateTimeOffset(
        JsonElement element,
        string propertyName)
    {
        var value = TryGetString(element, propertyName);
        return DateTimeOffset.TryParse(value, out var date)
            ? date
            : null;
    }

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

    private static void ValidateRequest(AzureAiSearchKnowledgeBaseRetrievalRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.KnowledgeBaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.KnowledgeSourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Filter);
        ArgumentNullException.ThrowIfNull(request.ConversationHistory);
        if (request.RetrievalCandidateLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        if (request.FinalEvidenceLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        if (request.MaxRuntimeInSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        if (request.MaxOutputSizeInTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
    }

    private static Uri CreateRetrieveUri(
        string endpoint,
        string knowledgeBaseName)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
            || endpointUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "An HTTPS Azure AI Search endpoint is required.",
                nameof(endpoint));
        }

        return new Uri(
            endpointUri,
            $"/knowledgebases/{Uri.EscapeDataString(knowledgeBaseName)}/retrieve?api-version={ApiVersion}");
    }
}
