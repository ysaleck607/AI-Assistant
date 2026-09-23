using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AssistantCore.ExternalServices.Services.OpenAI;

public sealed class OpenAiEmbeddingsClient(HttpClient httpClient)
{
    public async Task<OpenAiEmbeddingBatchResult> CreateAsync(
        string endpoint,
        string apiKey,
        string model,
        int dimensions,
        IReadOnlyCollection<string> inputs,
        CancellationToken cancellationToken = default,
        string? deploymentName = null,
        string apiVersion = "2024-06-01")
    {
        var usesAzureOpenAi = !string.IsNullOrWhiteSpace(deploymentName);
        var requestUri = usesAzureOpenAi
            ? $"{endpoint.TrimEnd('/')}/openai/deployments/{Uri.EscapeDataString(deploymentName!)}/embeddings?api-version={Uri.EscapeDataString(apiVersion)}"
            : $"{endpoint.TrimEnd('/')}/embeddings";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            requestUri)
        {
            Content = usesAzureOpenAi
                ? JsonContent.Create(new { input = inputs, dimensions })
                : JsonContent.Create(new { model, input = inputs, dimensions })
        };
        if (usesAzureOpenAi)
        {
            request.Headers.TryAddWithoutValidation("api-key", apiKey);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var providerErrorMessage = await response.Content.ReadAsStringAsync(cancellationToken);
            var retryAfter = response.Headers.RetryAfter;
            var retryAfterDelay = retryAfter?.Delta
                ?? (retryAfter?.Date is { } retryAfterAt
                    ? retryAfterAt - DateTimeOffset.UtcNow
                    : null);
            throw new OpenAiExternalException(
                (int)response.StatusCode,
                providerErrorMessage,
                retryAfterDelay is { } delay && delay > TimeSpan.Zero ? delay : null,
                retryAfter?.Date);
        }

        var payload = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken)
            ?? throw new OpenAiExternalException(-1);
        var vectors = payload.Data.OrderBy(item => item.Index).Select(item => item.Embedding).ToArray();
        if (vectors.Length != inputs.Count
            || vectors.Any(vector => vector.Count != dimensions
                || vector.Any(value => float.IsNaN(value) || float.IsInfinity(value))))
        {
            throw new OpenAiExternalException(-1);
        }

        return new OpenAiEmbeddingBatchResult(vectors, payload.Usage?.TotalTokens ?? 0);
    }

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("data")] IReadOnlyCollection<EmbeddingItem> Data,
        [property: JsonPropertyName("usage")] EmbeddingUsage? Usage);

    private sealed record EmbeddingItem(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] IReadOnlyList<float> Embedding);

    private sealed record EmbeddingUsage(
        [property: JsonPropertyName("total_tokens")] int TotalTokens);
}
