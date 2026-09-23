using System.Net;
using AssistantCore.ExternalServices.Services.OpenAI;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Services.LlmQuota;
using AssistantCore.Service.Infrastructure.Microsoft365;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365EmbeddingGeneratorAdapterTests
{
    [Fact]
    public async Task Given_NoContent_When_CreateAsync_Then_ReturnsEmptyAndNeverRecordsConsumption()
    {
        // Given
        var tracker = new RecordingLlmTokenConsumptionTracker();
        var adapter = CreateAdapter(tracker, batchSize: 32, responseTokensPerCall: 0);

        // When
        var vectors = await adapter.CreateAsync([], CancellationToken.None);

        // Then
        Assert.Empty(vectors);
        Assert.Empty(tracker.RecordedConsumptions);
    }

    [Fact]
    public async Task Given_ContentFittingInOneBatch_When_CreateAsync_Then_RecordsThatBatchsTokens()
    {
        // Given
        var tracker = new RecordingLlmTokenConsumptionTracker();
        var adapter = CreateAdapter(tracker, batchSize: 32, responseTokensPerCall: 12);

        // When
        await adapter.CreateAsync(["chunk-1"], CancellationToken.None);

        // Then
        var recorded = Assert.Single(tracker.RecordedConsumptions);
        Assert.Equal("text-embedding-3-small", recorded.Model);
        Assert.Equal(12, recorded.Tokens);
    }

    [Fact]
    public async Task Given_ContentSpanningMultipleBatches_When_CreateAsync_Then_RecordsTheSummedTokensOnce()
    {
        // Given : 3 contenus, taille de lot 1 -> 3 appels HTTP, un seul enregistrement
        // de consommation avec le total, pas un par lot.
        var tracker = new RecordingLlmTokenConsumptionTracker();
        var adapter = CreateAdapter(tracker, batchSize: 1, responseTokensPerCall: 10);

        // When
        await adapter.CreateAsync(["chunk-1", "chunk-2", "chunk-3"], CancellationToken.None);

        // Then
        var recorded = Assert.Single(tracker.RecordedConsumptions);
        Assert.Equal(30, recorded.Tokens);
    }

    [Fact]
    public async Task Given_TokenTrackingFails_When_CreateAsync_Then_StillReturnsTheVectors()
    {
        // Given : le suivi de quota ne doit jamais faire echouer l'indexation.
        var adapter = CreateAdapter(
            new ThrowingLlmTokenConsumptionTracker(),
            batchSize: 32,
            responseTokensPerCall: 12);

        // When
        var vectors = await adapter.CreateAsync(["chunk-1"], CancellationToken.None);

        // Then
        Assert.Single(vectors);
    }

    private static Microsoft365EmbeddingGeneratorAdapter CreateAdapter(
        ILlmTokenConsumptionTracker tracker,
        int batchSize,
        int responseTokensPerCall)
    {
        var responseBody =
            "{\"data\":[{\"index\":0,\"embedding\":[0.1,0.2]}],\"usage\":{\"total_tokens\":"
            + responseTokensPerCall
            + "}}";
        // Pas de "using" ici : le client HTTP doit survivre a cette methode, l'adaptateur
        // retourne le garde jusqu'a la fin du test.
        var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody)
            }));
        var client = new OpenAiEmbeddingsClient(httpClient);
        var options = Options.Create(new Microsoft365Options
        {
            EmbeddingEndpoint = "https://api.openai.com/v1",
            EmbeddingApiKey = "test-key",
            EmbeddingModel = "text-embedding-3-small",
            EmbeddingDimensions = 2,
            EmbeddingBatchSize = batchSize,
        });

        return new Microsoft365EmbeddingGeneratorAdapter(
            client,
            options,
            tracker,
            NullLogger<Microsoft365EmbeddingGeneratorAdapter>.Instance);
    }

    private sealed class RecordingLlmTokenConsumptionTracker : ILlmTokenConsumptionTracker
    {
        public List<(string Model, long Tokens)> RecordedConsumptions { get; } = [];

        public Task RecordConsumptionAsync(
            string model,
            long tokens,
            CancellationToken cancellationToken = default)
        {
            RecordedConsumptions.Add((model, tokens));
            return Task.CompletedTask;
        }

        public Task<long> GetCurrentPeriodConsumptionAsync(
            string model,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0L);
    }

    private sealed class ThrowingLlmTokenConsumptionTracker : ILlmTokenConsumptionTracker
    {
        public Task RecordConsumptionAsync(
            string model,
            long tokens,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated tracking failure.");

        public Task<long> GetCurrentPeriodConsumptionAsync(
            string model,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0L);
    }
}
