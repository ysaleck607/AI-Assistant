using System.Net;
using System.Text.Json;
using AssistantCore.ExternalServices.Services.OpenAI;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class OpenAiEmbeddingsClientTests
{
    [Theory, AutoDomainData]
    public async Task Given_ValidInputs_When_CreateAsync_Then_ReturnsVectorsInProviderOrder(
        string apiKey,
        string firstInput,
        string secondInput)
    {
        // Given
        string? requestPayload = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestPayload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"data":[
                      {"index":1,"embedding":[0.3,0.4]},
                      {"index":0,"embedding":[0.1,0.2]}
                    ]}
                    """)
            };
        }));
        var client = new OpenAiEmbeddingsClient(httpClient);

        // When
        var result = await client.CreateAsync(
            "https://api.openai.com/v1",
            apiKey,
            "text-embedding-3-small",
            2,
            [firstInput, secondInput],
            CancellationToken.None);

        // Then
        Assert.Equal([0.1f, 0.2f], result.Vectors[0]);
        Assert.Equal([0.3f, 0.4f], result.Vectors[1]);
        using var payload = JsonDocument.Parse(requestPayload!);
        Assert.Equal(2, payload.RootElement.GetProperty("dimensions").GetInt32());
        Assert.Equal(2, payload.RootElement.GetProperty("input").GetArrayLength());
    }

    [Theory, AutoDomainData]
    public async Task Given_AUsageFieldInTheResponse_When_CreateAsync_Then_ReturnsTheTotalTokens(
        string apiKey,
        string input)
    {
        // Given
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"data":[{"index":0,"embedding":[0.1,0.2]}],"usage":{"prompt_tokens":12,"total_tokens":12}}
                    """)
            }));
        var client = new OpenAiEmbeddingsClient(httpClient);

        // When
        var result = await client.CreateAsync(
            "https://api.openai.com/v1",
            apiKey,
            "text-embedding-3-small",
            2,
            [input],
            CancellationToken.None);

        // Then
        Assert.Equal(12, result.TotalTokens);
    }

    [Theory, AutoDomainData]
    public async Task Given_NoUsageFieldInTheResponse_When_CreateAsync_Then_TotalTokensIsZero(
        string apiKey,
        string input)
    {
        // Given
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"data":[{"index":0,"embedding":[0.1,0.2]}]}
                    """)
            }));
        var client = new OpenAiEmbeddingsClient(httpClient);

        // When
        var result = await client.CreateAsync(
            "https://api.openai.com/v1",
            apiKey,
            "text-embedding-3-small",
            2,
            [input],
            CancellationToken.None);

        // Then
        Assert.Equal(0, result.TotalTokens);
    }

    [Theory, AutoDomainData]
    public async Task Given_AzureOpenAiDeployment_When_CreateAsync_Then_UsesDeploymentRouteAndApiKey(
        string apiKey,
        string input)
    {
        // Given
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"data":[{"index":0,"embedding":[0.1,0.2]}]}
                    """)
            };
        }));
        var client = new OpenAiEmbeddingsClient(httpClient);

        // When
        await client.CreateAsync(
            "https://embedding.openai.azure.com/",
            apiKey,
            "text-embedding-3-small",
            2,
            [input],
            CancellationToken.None,
            "m365-text-embedding-3-small",
            "2024-06-01");

        // Then
        Assert.Equal(
            "https://embedding.openai.azure.com/openai/deployments/m365-text-embedding-3-small/embeddings?api-version=2024-06-01",
            capturedRequest!.RequestUri!.AbsoluteUri);
        Assert.Equal(apiKey, capturedRequest.Headers.GetValues("api-key").Single());
        Assert.Null(capturedRequest.Headers.Authorization);
    }
}
