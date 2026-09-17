using AssistantCore.ExternalServices.Services.OpenAI;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Infrastructure.Microsoft365;

public sealed class Microsoft365EmbeddingGeneratorAdapter(
    OpenAiEmbeddingsClient client,
    IOptions<Microsoft365Options> options) : IMicrosoft365EmbeddingGenerator
{
    private const int MaximumTransientAttempts = 6;
    private static readonly SemaphoreSlim EmbeddingGate = new(1, 1);

    public async Task<IReadOnlyList<IReadOnlyList<float>>> CreateAsync(
        IReadOnlyCollection<string> contents,
        CancellationToken cancellationToken = default)
    {
        if (contents.Count == 0)
        {
            return [];
        }

        var configuration = options.Value;
        if (string.IsNullOrWhiteSpace(configuration.EmbeddingApiKey))
        {
            throw new InvalidOperationException("Microsoft365 embedding API key is required.");
        }

        await EmbeddingGate.WaitAsync(cancellationToken);
        try
        {
            var vectors = new List<IReadOnlyList<float>>(contents.Count);
            foreach (var batch in contents.Chunk(configuration.EmbeddingBatchSize))
            {
                vectors.AddRange(await CreateBatchWithRetryAsync(
                    client,
                    configuration,
                    batch,
                    cancellationToken));
            }

            return vectors;
        }
        finally
        {
            EmbeddingGate.Release();
        }
    }

    private static async Task<IReadOnlyList<IReadOnlyList<float>>> CreateBatchWithRetryAsync(
        OpenAiEmbeddingsClient client,
        Microsoft365Options configuration,
        string[] batch,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await client.CreateAsync(
                    configuration.EmbeddingEndpoint,
                    configuration.EmbeddingApiKey,
                    configuration.EmbeddingModel,
                    configuration.EmbeddingDimensions,
                    batch,
                    cancellationToken,
                    configuration.EmbeddingDeploymentName,
                    configuration.EmbeddingApiVersion);
            }
            catch (OpenAiExternalException exception) when (
                exception.IsTransient
                && attempt < MaximumTransientAttempts - 1)
            {
                var delay = exception.RetryAfterDelay
                    ?? TimeSpan.FromSeconds(Math.Min(300, 30 * Math.Pow(2, attempt)));
                await Task.Delay(delay, cancellationToken);
            }
            catch (OpenAiExternalException exception)
            {
                throw new Microsoft365ExternalException(
                    "The Microsoft365 embedding request failed.",
                    exception,
                    exception.IsTransient,
                    exception.RetryAfterDelay);
            }
        }
    }
}
