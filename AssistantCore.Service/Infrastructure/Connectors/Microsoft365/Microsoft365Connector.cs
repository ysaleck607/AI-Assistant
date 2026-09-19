using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Models.Messages.AgenticRetrieval;
using AssistantCore.Service.Application.Models.Messages.Connectors;
using AssistantCore.Service.Application.Models.Messages.Connectors.Microsoft365;
using AssistantCore.Service.Application.Models.Messages.Evidence;
using AssistantCore.Service.Application.Models.Messages.Tools.Arguments;
using AssistantCore.Service.Application.Services.Messages.Connectors;
using AssistantCore.Service.Application.Services.Messages.Connectors.Microsoft365;
using AssistantCore.Service.Application.Services.Messages.Evidence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Infrastructure.Connectors.Microsoft365;

public sealed class Microsoft365Connector(
    IMicrosoft365UserGroupResolver groupResolver,
    IMicrosoft365SharePointGroupResolver sharePointGroupResolver,
    IAgenticRetrievalClient agenticRetrievalClient,
    Microsoft365ConnectorOptions options,
    IOptions<AzureAiSearchOptions> searchOptions,
    IEvidenceNormalizer evidenceNormalizer,
    ILogger<Microsoft365Connector>? logger = null) : IMicrosoft365Connector
{
    public async Task<ConnectorResult> SearchAsync(
        SearchMicrosoft365ToolArguments request,
        ConnectorExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        EnsureMicrosoftIdentity(context);

        var normalizedUserId = context.EntraUserId!.Value.ToString("D");
        var groupResolutionStartedAt = TimeProvider.System.GetTimestamp();
        var entraGroupsTask = groupResolver.ResolveGroupIdsAsync(
            context.ExternalTenantId!,
            normalizedUserId,
            cancellationToken);
        var sharePointGroupsTask = sharePointGroupResolver.ResolveGroupIdsAsync(
            context.OrganizationId,
            context.ExternalTenantId!,
            normalizedUserId,
            cancellationToken);

        try
        {
            await Task.WhenAll(entraGroupsTask, sharePointGroupsTask);
        }
        catch (Exception exception)
        {
            logger?.LogError(
                exception,
                "Microsoft 365 search failed during group resolution. Entra task status: {EntraTaskStatus}; SharePoint task status: {SharePointTaskStatus}.",
                entraGroupsTask.Status,
                sharePointGroupsTask.Status);
            throw;
        }
        var groupIds = await entraGroupsTask;
        var sharePointGroupIds = await sharePointGroupsTask;
        logger?.LogInformation(
            "Microsoft365 group resolution completed in {ElapsedMilliseconds} ms with {EntraGroupCount} Entra groups and {SharePointGroupCount} SharePoint groups.",
            TimeProvider.System.GetElapsedTime(groupResolutionStartedAt).TotalMilliseconds,
            groupIds.Count,
            sharePointGroupIds.Count);

        var searchParameters = new Microsoft365SearchParameters(
            request.Query,
            request.SourceTypes,
            request.DateFrom,
            request.DateTo,
            new Microsoft365SearchSecurityContext(
                context.OrganizationId,
                normalizedUserId,
                groupIds,
                sharePointGroupIds),
            context.RetrievalCandidateLimit);

        return await SearchKnowledgeBaseAsync(
            request,
            context,
            searchParameters,
            cancellationToken);
    }

    private async Task<ConnectorResult> SearchKnowledgeBaseAsync(
        SearchMicrosoft365ToolArguments request,
        ConnectorExecutionContext context,
        Microsoft365SearchParameters searchParameters,
        CancellationToken cancellationToken)
    {
        var configuration = searchOptions.Value;
        var filter = Microsoft365SearchFilterBuilder.Build(searchParameters);
        var retrievalStartedAt = TimeProvider.System.GetTimestamp();
        var result = await agenticRetrievalClient.RetrieveAsync(
            new AgenticRetrievalRequest(
                request.Query,
                [],
                configuration.KnowledgeBaseName,
                configuration.KnowledgeSourceName,
                filter,
                context.RetrievalCandidateLimit,
                options.MaximumResults,
                configuration.KnowledgeBaseMaxRuntimeInSeconds,
                configuration.KnowledgeBaseMaxOutputSizeInTokens
                    ?? throw new InvalidOperationException(
                        "AzureSearch knowledge base output token limit is required.")),
            cancellationToken);
        logger?.LogInformation(
            "Microsoft365 knowledge base retrieval completed in {ElapsedMilliseconds} ms with {ReferenceCount} references.",
            TimeProvider.System.GetElapsedTime(retrievalStartedAt).TotalMilliseconds,
            result.References.Count);

        var records = result.References
            .Select(reference => new Microsoft365SearchRecord(
                "Microsoft365",
                reference.Title,
                reference.Content,
                reference.DocumentKey,
                reference.SiteId,
                reference.DriveId,
                reference.DriveItemId,
                reference.Url,
                reference.ModifiedAt,
                reference.RelevanceScore,
                reference.RelevanceScore,
                reference.ArchivePath))
            .Where(record => record.RelevanceScore is null
                || record.RelevanceScore >= configuration.MinimumSemanticRelevanceScore)
            .ToArray();

        logger?.LogInformation(
            "Microsoft365 knowledge base relevance stage kept {RelevantCount} relevant records from {ReferenceCount} references.",
            records.Length,
            result.References.Count);

        var evidence = evidenceNormalizer.Normalize(
            records.Select(MapCandidate).ToArray(),
            new EvidenceNormalizationOptions(
                options.MaximumContentLength,
                options.MaximumResults));

        LogAgenticRetrieval(result.Activity, result.References.Count, records.Length);
        return new ConnectorResult(evidence);
    }

    private void LogAgenticRetrieval(
        IReadOnlyCollection<AgenticRetrievalActivity> activity,
        int recordsBeforeFiltering,
        int recordsAfterRelevanceFiltering)
    {
        var subqueryCount = activity.Count(item =>
            string.Equals(item.Type, "searchIndex", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(item.Search));

        logger?.LogInformation(
            "Microsoft365 agentic retrieval completed with {SubqueryCount} observed subqueries, {RecordsBeforeFiltering} references before filtering and {RecordsAfterRelevanceFiltering} relevant records.",
            subqueryCount,
            recordsBeforeFiltering,
            recordsAfterRelevanceFiltering);
    }

    private static void EnsureMicrosoftIdentity(ConnectorExecutionContext context)
    {
        if (context.OrganizationId == Guid.Empty
            || context.MemberId == Guid.Empty
            || context.IdentityProvider != IdentityProvider.MicrosoftEntraId
            || string.IsNullOrWhiteSpace(context.ExternalTenantId)
            || context.EntraUserId is null
            || context.EntraUserId == Guid.Empty
            || string.IsNullOrWhiteSpace(context.UserEmail))
        {
            throw new InvalidOperationException(
                "The authenticated member cannot be resolved to a Microsoft Entra identity.");
        }
    }

    private static EvidenceCandidate MapCandidate(Microsoft365SearchRecord record) => new(
        record.SourceType,
        record.Title,
        record.Content,
        record.Reference,
        record.Url,
        record.ModifiedAt,
        record.RelevanceScore);
}
