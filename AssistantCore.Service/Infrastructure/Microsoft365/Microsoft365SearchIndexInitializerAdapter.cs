using AssistantCore.ExternalServices.Services.Azure;
using AssistantCore.ExternalServices.Entities.Azure;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Infrastructure.Microsoft365;

public sealed class Microsoft365SearchIndexInitializerAdapter(
    AzureAiSearchIndexClient client,
    IOptions<AzureAiSearchOptions> searchOptions,
    IOptions<Microsoft365Options> microsoft365Options) : IMicrosoft365SearchIndexInitializer
{
    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        var search = searchOptions.Value;
        var microsoft365 = microsoft365Options.Value;
        if (!search.EnsureIndexOnStartup)
        {
            return;
        }

        try
        {
            await client.EnsureCreatedAsync(
                search.Endpoint,
                search.IndexName,
                search.ApiKey,
                microsoft365.EmbeddingDimensions,
                search.SemanticConfigurationName,
                cancellationToken,
                search.VectorSearchMetric,
                search.KnowledgeSourceName,
                search.KnowledgeBaseName,
                search.KnowledgeBaseRetrievalReasoningEffort.Trim().ToLowerInvariant(),
                search.VectorizerName,
                CreateModelConfiguration(
                    microsoft365.EmbeddingEndpoint,
                    microsoft365.EmbeddingDeploymentName,
                    microsoft365.EmbeddingModel,
                    microsoft365.EmbeddingApiKey),
                CreateModelConfiguration(
                    search.PlanningModelEndpoint,
                    search.PlanningModelDeploymentName,
                    search.PlanningModelName,
                    search.PlanningModelApiKey));
        }
        catch (AzureAiSearchExternalException exception)
        {
            throw new AzureAiSearchUnavailableException(
                "Azure AI Search index initialization failed.",
                exception);
        }
    }

    private static AzureOpenAiModelConfiguration? CreateModelConfiguration(
        string endpoint,
        string deploymentName,
        string modelName,
        string apiKey) =>
        string.IsNullOrWhiteSpace(deploymentName)
            ? null
            : new AzureOpenAiModelConfiguration(endpoint, deploymentName, modelName, apiKey);
}
