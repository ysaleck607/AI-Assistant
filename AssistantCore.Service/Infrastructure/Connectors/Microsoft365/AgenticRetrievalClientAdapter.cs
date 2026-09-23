using AssistantCore.ExternalServices.Entities.Azure;
using AssistantCore.ExternalServices.Services.Azure;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Messages.AgenticRetrieval;
using AssistantCore.Service.Application.Models.Messages.AiModels;
using AssistantCore.Service.Application.Services.Messages.Connectors.Microsoft365;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Infrastructure.Connectors.Microsoft365;

public sealed class AgenticRetrievalClientAdapter(
    AzureAiSearchKnowledgeBaseRetrievalClient client,
    IOptions<AzureAiSearchOptions> options,
    ILogger<AgenticRetrievalClientAdapter>? logger = null) : IAgenticRetrievalClient
{
    public async Task<AgenticRetrievalResult> RetrieveAsync(
        AgenticRetrievalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var configuration = options.Value;
        if (string.IsNullOrWhiteSpace(configuration.Endpoint)
            || string.IsNullOrWhiteSpace(request.KnowledgeBaseName)
            || string.IsNullOrWhiteSpace(request.KnowledgeSourceName))
        {
            throw new InvalidOperationException(
                "AzureSearch endpoint, knowledge base and knowledge source are required for agentic retrieval.");
        }

        AzureAiSearchKnowledgeBaseRetrievalResult result;
        var startedAt = TimeProvider.System.GetTimestamp();
        try
        {
            result = await client.RetrieveAsync(
                configuration.Endpoint,
                configuration.ApiKey,
                new AzureAiSearchKnowledgeBaseRetrievalRequest(
                    request.KnowledgeBaseName,
                    request.KnowledgeSourceName,
                    request.Query,
                    request.ConversationHistory.Select(MapMessage).ToArray(),
                    request.Filter,
                    request.RetrievalCandidateLimit,
                    request.FinalEvidenceLimit,
                    request.MaxRuntimeInSeconds,
                    request.MaxOutputSizeInTokens,
                    configuration.KnowledgeBaseRetrievalReasoningEffort),
                cancellationToken);

            logger?.LogInformation(
                "Azure AI Search knowledge retrieval completed in {ElapsedMilliseconds} ms using {ReasoningEffort} reasoning with {ReferenceCount} references.",
                TimeProvider.System.GetElapsedTime(startedAt).TotalMilliseconds,
                configuration.KnowledgeBaseRetrievalReasoningEffort,
                result.References.Count);
        }
        catch (AzureAiSearchExternalException exception)
        {
            logger?.LogError(
                exception,
                "Azure AI Search knowledge retrieval failed for knowledge base {KnowledgeBaseName} and source {KnowledgeSourceName}.",
                request.KnowledgeBaseName,
                request.KnowledgeSourceName);
            throw new Microsoft365ExternalException(
                "Microsoft 365 agentic retrieval failed.",
                exception);
        }

        return new AgenticRetrievalResult(
            result.MergedContent,
            result.References.Select(reference => new AgenticRetrievalReference(
                reference.ReferenceId,
                reference.DocumentKey,
                reference.Title,
                reference.Content,
                reference.SiteId,
                reference.DriveId,
                reference.DriveItemId,
                reference.Url,
                reference.ModifiedAt,
                reference.RelevanceScore,
                reference.ArchivePath)).ToArray(),
            result.Activity.Select(activity => new AgenticRetrievalActivity(
                activity.Type,
                activity.KnowledgeSourceName,
                activity.Search,
                activity.Count,
                activity.ElapsedMilliseconds)).ToArray());
    }

    private static AzureAiSearchKnowledgeBaseMessage MapMessage(
        AgenticRetrievalMessage message) =>
        new(
            message.Role == AiConversationRole.Assistant ? "assistant" : "user",
            message.Content);
}
