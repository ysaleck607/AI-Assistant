using System.Net;
using System.Text.Json;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AssistantCore.ExternalServices.Entities.Azure;
using Azure.Core;
using Azure.Identity;

namespace AssistantCore.ExternalServices.Services.Azure;

public sealed class AzureAiSearchIndexClient
{
    private const string ApiVersion = "2025-09-01";
    private static readonly string[] SearchScopes = ["https://search.azure.com/.default"];
    private readonly HttpClient httpClient;
    private readonly TokenCredential credential;

    public AzureAiSearchIndexClient(HttpClient httpClient)
        : this(httpClient, new DefaultAzureCredential())
    {
    }

    internal AzureAiSearchIndexClient(HttpClient httpClient, TokenCredential credential)
    {
        this.httpClient = httpClient;
        this.credential = credential;
    }

    public async Task EnsureCreatedAsync(
        string endpoint,
        string indexName,
        string? apiKey,
        int embeddingDimensions,
        string semanticConfigurationName,
        CancellationToken cancellationToken = default,
        string vectorMetric = "cosine",
        string? knowledgeSourceName = null,
        string? knowledgeBaseName = null,
        string retrievalReasoningEffort = "minimal",
        string vectorizerName = "m365-azure-openai-vectorizer",
        AzureOpenAiModelConfiguration? vectorizerModel = null,
        AzureOpenAiModelConfiguration? planningModel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(semanticConfigurationName);
        if (vectorMetric != "cosine") throw new ArgumentException("The configured embedding strategy requires cosine.", nameof(vectorMetric));
        Activity.Current?.SetTag("rag.vector.metric", vectorMetric);

        var uri = new Uri(
            new Uri(endpoint),
            $"/indexes/{Uri.EscapeDataString(indexName)}?api-version={ApiVersion}");
        using var get = new HttpRequestMessage(HttpMethod.Get, uri);
        await AuthorizeAsync(get, apiKey, cancellationToken);
        using var existing = await httpClient.SendAsync(get, cancellationToken);
        if (!existing.IsSuccessStatusCode && existing.StatusCode != HttpStatusCode.NotFound)
        {
            var errorBody = await existing.Content.ReadAsStringAsync(cancellationToken);
            throw new AzureAiSearchExternalException(
                $"Azure AI Search index validation failed with status {(int)existing.StatusCode}: {errorBody}");
        }

        if (existing.IsSuccessStatusCode)
        {
            using var definition = await JsonDocument.ParseAsync(await existing.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (definition.RootElement.TryGetProperty("vectorSearch", out var vectorSearch)
                && vectorSearch.TryGetProperty("algorithms", out var algorithms))
            {
                foreach (var algorithm in algorithms.EnumerateArray())
                {
                    if (algorithm.GetProperty("name").GetString() == "m365-hnsw"
                        && algorithm.TryGetProperty("hnswParameters", out var parameters)
                        && parameters.TryGetProperty("metric", out var metric)
                        && metric.GetString() != vectorMetric)
                        throw new AzureAiSearchExternalException("Vector metric migration requires a new index and full reingestion; the existing index was not changed.");
                }
            }
        }

        var fields = AzureAiSearchMicrosoft365IndexDefinition.CreateFields()
            .Select(field => CreateField(field, embeddingDimensions))
            .ToArray();
        using var put = new HttpRequestMessage(HttpMethod.Put, uri)
        {
            Content = JsonContent.Create(new
            {
                name = indexName,
                fields,
                vectorSearch = new
                {
                    algorithms = new[] { new { name = "m365-hnsw", kind = "hnsw", hnswParameters = new { metric = vectorMetric } } },
                    profiles = new[]
                    {
                        new
                        {
                            name = "m365-vector-profile",
                            algorithm = "m365-hnsw",
                            vectorizer = vectorizerModel is null ? null : vectorizerName
                        }
                    },
                    vectorizers = vectorizerModel is null
                        ? null
                        : new[]
                        {
                            new
                            {
                                name = vectorizerName,
                                kind = "azureOpenAI",
                                azureOpenAIParameters = new
                                {
                                    resourceUri = vectorizerModel.ResourceUri,
                                    deploymentId = vectorizerModel.DeploymentId,
                                    modelName = vectorizerModel.ModelName,
                                    apiKey = vectorizerModel.ApiKey
                                }
                            }
                        }
                },
                semantic = new
                {
                    configurations = new[]
                    {
                        new
                        {
                            name = semanticConfigurationName,
                            prioritizedFields = new
                            {
                                titleField = new { fieldName = "title" },
                                prioritizedContentFields = new[] { new { fieldName = "content" } },
                                prioritizedKeywordsFields = Array.Empty<object>()
                            }
                        }
                    }
                }
            })
        };
        await AuthorizeAsync(put, apiKey, cancellationToken);
        using var created = await httpClient.SendAsync(put, cancellationToken);
        if (!created.IsSuccessStatusCode)
        {
            var errorBody = await created.Content.ReadAsStringAsync(cancellationToken);
            throw new AzureAiSearchExternalException(
                $"Azure AI Search index creation failed with status {(int)created.StatusCode}: {errorBody}");
        }

        if (!string.IsNullOrWhiteSpace(knowledgeSourceName)
            && !string.IsNullOrWhiteSpace(knowledgeBaseName))
        {
            await EnsureKnowledgeSourceCreatedAsync(
                endpoint,
                indexName,
                apiKey,
                knowledgeSourceName,
                cancellationToken);
            await EnsureKnowledgeBaseCreatedAsync(
                endpoint,
                apiKey,
                knowledgeSourceName,
                knowledgeBaseName,
                retrievalReasoningEffort,
                planningModel,
                cancellationToken);
        }
    }

    /// <summary>
    /// Reads the search SERVICE's own storage counters (shared across every index on
    /// it, not just the one this app uses) - the real Azure-side quota, distinct from
    /// anything this application tracks itself.
    /// </summary>
    public async Task<AzureAiSearchServiceStatistics> GetServiceStatisticsAsync(
        string endpoint,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        var uri = new Uri(new Uri(endpoint), $"/servicestats?api-version={ApiVersion}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        await AuthorizeAsync(request, apiKey, cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new AzureAiSearchExternalException(
                $"Azure AI Search service statistics request failed with status {(int)response.StatusCode}: {errorBody}");
        }

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var storage = document.RootElement.GetProperty("counters").GetProperty("storageSize");
        return new AzureAiSearchServiceStatistics(
            storage.GetProperty("usage").GetInt64(),
            storage.GetProperty("quota").GetInt64());
    }

    private async Task EnsureKnowledgeSourceCreatedAsync(
        string endpoint,
        string indexName,
        string? apiKey,
        string knowledgeSourceName,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            new Uri(
                new Uri(endpoint),
                $"/knowledgesources/{Uri.EscapeDataString(knowledgeSourceName)}?api-version=2026-08-01-preview"))
        {
            Content = JsonContent.Create(new
            {
                name = knowledgeSourceName,
                kind = "searchIndex",
                description = "Microsoft 365 indexed content knowledge source for Synaptix.",
                encryptionKey = (object?)null,
                searchIndexParameters = new
                {
                    searchIndexName = indexName,
                    searchFields = new[]
                    {
                        new { name = "title" },
                        new { name = "content" }
                    },
                    sourceDataFields = new[]
                    {
                        new { name = "chunkId" },
                        new { name = "archivePath" },
                        new { name = "sourceType" },
                        new { name = "title" },
                        new { name = "content" },
                        new { name = "siteId" },
                        new { name = "driveId" },
                        new { name = "driveItemId" },
                        new { name = "url" },
                        new { name = "modifiedAt" }
                    }
                }
            })
        };
        await AuthorizeAsync(request, apiKey, cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new AzureAiSearchExternalException(
                $"Azure AI Search knowledge source creation failed with status {(int)response.StatusCode}: {errorBody}");
        }
    }

    private async Task EnsureKnowledgeBaseCreatedAsync(
        string endpoint,
        string? apiKey,
        string knowledgeSourceName,
        string knowledgeBaseName,
        string retrievalReasoningEffort,
        AzureOpenAiModelConfiguration? planningModel,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            new Uri(
                new Uri(endpoint),
                $"/knowledgebases/{Uri.EscapeDataString(knowledgeBaseName)}?api-version=2026-08-01-preview"))
        {
            Content = JsonContent.Create(new
            {
                name = knowledgeBaseName,
                description = "Synaptix knowledge base for authorized Microsoft 365 retrieval.",
                knowledgeSources = new[]
                {
                    new
                    {
                        name = knowledgeSourceName
                    }
                },
                outputMode = "extractiveData",
                retrievalReasoningEffort = new
                {
                    kind = retrievalReasoningEffort
                },
                models = planningModel is null
                    ? null
                    : new[]
                    {
                        new
                        {
                            kind = "azureOpenAI",
                            azureOpenAIParameters = new
                            {
                                resourceUri = planningModel.ResourceUri,
                                deploymentId = planningModel.DeploymentId,
                                modelName = planningModel.ModelName,
                                apiKey = planningModel.ApiKey
                            }
                        }
                    },
                encryptionKey = (object?)null
            })
        };
        await AuthorizeAsync(request, apiKey, cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new AzureAiSearchExternalException(
                $"Azure AI Search knowledge base creation failed with status {(int)response.StatusCode}: {errorBody}");
        }
    }

    private static object CreateField(AzureAiSearchIndexFieldDefinition field, int dimensions) =>
        field.Name == "contentVector"
            ? new
            {
                name = field.Name,
                type = field.Type,
                key = field.Key,
                searchable = true,
                filterable = field.Filterable,
                retrievable = false,
                dimensions = (int?)dimensions,
                vectorSearchProfile = (string?)"m365-vector-profile"
            }
            : new
            {
                name = field.Name,
                type = field.Type,
                key = field.Key,
                searchable = field.Searchable,
                filterable = field.Filterable,
                retrievable = field.Retrievable,
                dimensions = (int?)null,
                vectorSearchProfile = (string?)null
            };

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

        var token = await credential.GetTokenAsync(new TokenRequestContext(SearchScopes), cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
    }
}
