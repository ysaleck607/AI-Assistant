using System.Net;
using System.Text.Json;
using AssistantCore.ExternalServices.Services.Azure;
using AssistantCore.ExternalServices.Entities.Azure;

namespace AssistantCore.Service.Tests.Rag;

public sealed class VectorMetricTests
{
    [Theory, AutoDomainData]
    public async Task Given_NewIndex_When_EnsureCreatedAsync_Then_DeclaresCosine(Guid _)
    {
        // Given
        using var handler = new IndexHandler();
        using var http = new HttpClient(handler);
        // When
        await new AzureAiSearchIndexClient(http).EnsureCreatedAsync("https://search.example", "index", "key", 1536, "semantic");
        // Then
        using var definition = JsonDocument.Parse(handler.Definition!);
        Assert.Equal("cosine", definition.RootElement.GetProperty("vectorSearch").GetProperty("algorithms")[0]
            .GetProperty("hnswParameters").GetProperty("metric").GetString());
    }

    [Theory, AutoDomainData]
    public async Task Given_SearchableTextFields_When_EnsureCreatedAsync_Then_DeclaresAsciiFoldingAnalyzer(Guid _)
    {
        // Given
        using var handler = new IndexHandler();
        using var http = new HttpClient(handler);

        // When
        await new AzureAiSearchIndexClient(http).EnsureCreatedAsync(
            "https://search.example",
            "index",
            "key",
            1536,
            "semantic");

        // Then
        using var definition = JsonDocument.Parse(handler.Definition!);
        var fields = definition.RootElement.GetProperty("fields");
        Assert.All(
            fields.EnumerateArray().Where(field =>
                field.GetProperty("name").GetString() is "title" or "content"),
            field => Assert.Equal(
                AzureAiSearchMicrosoft365IndexDefinition.SearchableTextAnalyzer,
                field.GetProperty("analyzer").GetString()));
    }

    [Theory, AutoDomainData]
    public async Task Given_IncompatibleExistingIndex_When_EnsureCreatedAsync_Then_RequiresMigrationWithoutWriting(Guid _)
    {
        // Given
        using var handler = new IndexHandler("euclidean");
        using var http = new HttpClient(handler);
        // When
        var action = () => new AzureAiSearchIndexClient(http).EnsureCreatedAsync("https://search.example", "index", "key", 1536, "semantic");
        // Then
        await Assert.ThrowsAsync<AzureAiSearchExternalException>(action);
        Assert.Null(handler.Definition);
    }

    [Theory, AutoDomainData]
    public async Task Given_AzureOpenAiModel_When_EnsureCreatedAsync_Then_DeclaresMatchingVectorizer(
        string apiKey)
    {
        // Given
        using var handler = new IndexHandler();
        using var http = new HttpClient(handler);
        var model = new AzureOpenAiModelConfiguration(
            "https://embedding.openai.azure.com/",
            "m365-text-embedding-3-small",
            "text-embedding-3-small",
            apiKey);

        // When
        await new AzureAiSearchIndexClient(http).EnsureCreatedAsync(
            "https://search.example",
            "index",
            "key",
            1536,
            "semantic",
            vectorizerModel: model);

        // Then
        using var definition = JsonDocument.Parse(handler.Definition!);
        var vectorSearch = definition.RootElement.GetProperty("vectorSearch");
        Assert.Equal(
            "m365-azure-openai-vectorizer",
            vectorSearch.GetProperty("profiles")[0].GetProperty("vectorizer").GetString());
        var parameters = vectorSearch.GetProperty("vectorizers")[0].GetProperty("azureOpenAIParameters");
        Assert.Equal("m365-text-embedding-3-small", parameters.GetProperty("deploymentId").GetString());
        Assert.Equal("text-embedding-3-small", parameters.GetProperty("modelName").GetString());
    }

    private sealed class IndexHandler(string? metric = null) : HttpMessageHandler
    {
        public string? Definition { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
                return new(metric is null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
                { Content = new StringContent(JsonSerializer.Serialize(new { vectorSearch = new { algorithms = new[] { new { name = "m365-hnsw", hnswParameters = new { metric } } } } })) };
            Definition = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK);
        }
    }
}
