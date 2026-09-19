using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Models.Messages.AgenticRetrieval;
using AssistantCore.Service.Application.Models.Messages.AiModels;
using AssistantCore.Service.Application.Models.Messages.Connectors;
using AssistantCore.Service.Application.Models.Messages.Connectors.Microsoft365;
using AssistantCore.Service.Application.Models.Messages.Tools.Arguments;
using AssistantCore.Service.Application.Services.Messages.Connectors.Microsoft365;
using AssistantCore.Service.Application.Services.Messages.Evidence;
using AssistantCore.Service.Infrastructure.Connectors.Microsoft365;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365AgenticRetrievalConnectorTests
{
    [Theory, InlineAutoDomainData("code projet Atlas")]
    public async Task Given_AValidRequestAndPreviousTopic_When_SearchAsync_Then_RetrievesCurrentQueryWithoutConversationHistory(
        string query,
        Guid organizationId,
        Guid memberId,
        Guid entraUserId,
        Guid entraGroupId,
        string title,
        string content,
        string userEmail)
    {
        // Given
        var sharePointGroupId = "spg:contoso.sharepoint.com,site-collection-id,web-id:5";
        var retrievalClient = new RecordingAgenticRetrievalClient(
            new AgenticRetrievalReference(
                "0",
                "chunk-atlas",
                title,
                content,
                "site-id",
                "drive-id",
                "drive-item-id",
                "https://contoso.example/atlas",
                new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
                3.1d));
        var connector = CreateConnector(
            entraGroupId,
            sharePointGroupId,
            retrievalClient);
        var context = CreateContext(
            organizationId,
            memberId,
            entraUserId,
            userEmail) with
        {
            ConversationHistory =
            [
                new AiConversationMessage(AiConversationRole.User, "combien dois-je payer à Microsoft ?"),
                new AiConversationMessage(AiConversationRole.Assistant, "La facture Microsoft est de 26,22 $.")
            ]
        };

        // When
        var result = await connector.SearchAsync(
            new SearchMicrosoft365ToolArguments(
                query,
                ["sharepoint"],
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 31)),
            context,
            CancellationToken.None);

        // Then
        var request = Assert.Single(retrievalClient.ReceivedRequests);
        Assert.Equal("synaptix-m365-knowledge-base", request.KnowledgeBaseName);
        Assert.Equal("synaptix-m365-knowledge-source", request.KnowledgeSourceName);
        Assert.Equal(query, request.Query);
        Assert.Equal(50, request.RetrievalCandidateLimit);
        Assert.Equal(10, request.FinalEvidenceLimit);
        Assert.Empty(request.ConversationHistory);
        Assert.Contains($"organizationId eq '{organizationId:D}'", request.Filter, StringComparison.Ordinal);
        Assert.Contains($"allowedUserIds/any(id: id eq '{entraUserId:D}')", request.Filter, StringComparison.Ordinal);
        Assert.Contains(entraGroupId.ToString("D"), request.Filter, StringComparison.Ordinal);
        Assert.Contains(sharePointGroupId, request.Filter, StringComparison.Ordinal);
        Assert.Contains("sourceType eq 'sharepoint'", request.Filter, StringComparison.Ordinal);
        Assert.Contains("modifiedAt ge 2026-01-01T00:00:00Z", request.Filter, StringComparison.Ordinal);
        Assert.Contains("modifiedAt lt 2026-02-01T00:00:00Z", request.Filter, StringComparison.Ordinal);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(title, evidence.Title);
        Assert.Equal(content, evidence.Content);
    }

    [Theory, InlineAutoDomainData("compare les risques financiers d'Atlas et MécanoPlus")]
    public async Task Given_AValidRequest_When_SearchAsync_Then_UsesKnowledgeBaseRetrieval(
        string query,
        Guid organizationId,
        Guid memberId,
        Guid entraUserId,
        Guid entraGroupId,
        string userEmail)
    {
        // Given
        var retrievalClient = new RecordingAgenticRetrievalClient(
            CreateReference("0", "chunk-atlas", "Atlas", "Atlas risk content."));
        var connector = new Microsoft365Connector(
            new StaticMicrosoft365UserGroupResolver([entraGroupId.ToString("D")]),
            new StaticMicrosoft365SharePointGroupResolver([]),
            retrievalClient,
            CreateOptions(),
            CreateSearchOptions(),
            new EvidenceNormalizer(),
            logger: null);

        // When
        var result = await connector.SearchAsync(
            new SearchMicrosoft365ToolArguments(query, null, null, null),
            CreateContext(organizationId, memberId, entraUserId, userEmail),
            CancellationToken.None);

        // Then
        Assert.Single(retrievalClient.ReceivedRequests);
        Assert.Single(result.Evidence);
    }

    [Theory, InlineAutoDomainData("document autorise Atlas")]
    public async Task Given_AgenticRetrievalReturnsEvidence_When_SearchAsync_Then_KeepsSearchFilteredEvidence(
        string query,
        Guid organizationId,
        Guid memberId,
        Guid entraUserId,
        Guid entraGroupId,
        string userEmail)
    {
        // Given
        var connector = new Microsoft365Connector(
            new StaticMicrosoft365UserGroupResolver([entraGroupId.ToString("D")]),
            new StaticMicrosoft365SharePointGroupResolver([]),
            new RecordingAgenticRetrievalClient(
                CreateReference("0", "chunk-atlas", "Atlas", "Indexed authorized content.")),
            CreateOptions(),
            CreateSearchOptions(),
            new EvidenceNormalizer(),
            logger: null);

        // When
        var result = await connector.SearchAsync(
            new SearchMicrosoft365ToolArguments(query, null, null, null),
            CreateContext(organizationId, memberId, entraUserId, userEmail),
            CancellationToken.None);

        // Then
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal("Atlas", evidence.Title);
        Assert.Equal("Indexed authorized content.", evidence.Content);
    }

    private static Microsoft365Connector CreateConnector(
        Guid entraGroupId,
        string sharePointGroupId,
        IAgenticRetrievalClient retrievalClient) =>
        new(
            new StaticMicrosoft365UserGroupResolver([entraGroupId.ToString("D")]),
            new StaticMicrosoft365SharePointGroupResolver([sharePointGroupId]),
            retrievalClient,
            CreateOptions(),
            CreateSearchOptions(),
            new EvidenceNormalizer(),
            logger: null);

    private static ConnectorExecutionContext CreateContext(
        Guid organizationId,
        Guid memberId,
        Guid entraUserId,
        string userEmail) =>
        new(
            organizationId,
            memberId,
            Guid.NewGuid().ToString("D"),
            entraUserId,
            IdentityProvider.MicrosoftEntraId,
            RetrievalCandidateLimit: 50,
            UserEmail: userEmail);

    private static Microsoft365ConnectorOptions CreateOptions() =>
        new(10, 4000);

    private static IOptions<AzureAiSearchOptions> CreateSearchOptions() =>
        Options.Create(new AzureAiSearchOptions
        {
            Endpoint = "https://search.example",
            IndexName = "content-index",
            KnowledgeBaseName = "synaptix-m365-knowledge-base",
            KnowledgeSourceName = "synaptix-m365-knowledge-source",
            KnowledgeBaseMaxOutputSizeInTokens = 6000
        });

    private static AgenticRetrievalReference CreateReference(
        string referenceId,
        string documentKey,
        string title,
        string content) =>
        new(
            referenceId,
            documentKey,
            title,
            content,
            "site-id",
            "drive-id",
            "drive-item-id",
            "https://contoso.example/document",
            DateTimeOffset.UtcNow,
            3.0d);

    private sealed class RecordingAgenticRetrievalClient(
        params AgenticRetrievalReference[] references) : IAgenticRetrievalClient
    {
        public List<AgenticRetrievalRequest> ReceivedRequests { get; } = [];

        public Task<AgenticRetrievalResult> RetrieveAsync(
            AgenticRetrievalRequest request,
            CancellationToken cancellationToken)
        {
            ReceivedRequests.Add(request);
            return Task.FromResult(new AgenticRetrievalResult(
                "merged content",
                references,
                [
                    new AgenticRetrievalActivity(
                        "searchIndex",
                        request.KnowledgeSourceName,
                        "project Atlas financial risks",
                        1,
                        100),
                    new AgenticRetrievalActivity(
                        "searchIndex",
                        request.KnowledgeSourceName,
                        "MecanoPlus financial risks",
                        1,
                        100)
                ]));
        }
    }

    private sealed class StaticMicrosoft365UserGroupResolver(
        IReadOnlyCollection<string> groupIds) : IMicrosoft365UserGroupResolver
    {
        public Task<IReadOnlyCollection<string>> ResolveGroupIdsAsync(
            string externalTenantId,
            string entraUserId,
            CancellationToken cancellationToken) =>
            Task.FromResult(groupIds);
    }

    private sealed class StaticMicrosoft365SharePointGroupResolver(
        IReadOnlyCollection<string> groupIds) : IMicrosoft365SharePointGroupResolver
    {
        public Task<IReadOnlyCollection<string>> ResolveGroupIdsAsync(
            Guid organizationId,
            string externalTenantId,
            string userEmail,
            CancellationToken cancellationToken) =>
            Task.FromResult(groupIds);
    }
}
