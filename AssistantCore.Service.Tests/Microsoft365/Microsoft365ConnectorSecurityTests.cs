using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Models.Messages.AgenticRetrieval;
using AssistantCore.Service.Application.Models.Messages.Connectors;
using AssistantCore.Service.Application.Models.Messages.Connectors.Microsoft365;
using AssistantCore.Service.Application.Models.Messages.Tools.Arguments;
using AssistantCore.Service.Application.Services.Messages.Connectors.Microsoft365;
using AssistantCore.Service.Application.Services.Messages.Evidence;
using AssistantCore.Service.Infrastructure.Connectors.Microsoft365;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365ConnectorSecurityTests
{
    [Theory, AutoDomainData]
    public async Task Given_GroupResolutionFails_When_SearchAsync_Then_DoesNotSearchAzure(
        Guid organizationId,
        Guid memberId,
        Guid entraUserId,
        string query)
    {
        // Given
        var tenantId = Guid.NewGuid().ToString("D");
        var retrievalClient = new RecordingAgenticRetrievalClient();
        var connector = new Microsoft365Connector(
            new FailingMicrosoft365UserGroupResolver(),
            new EmptyMicrosoft365SharePointGroupResolver(),
            retrievalClient,
            new Microsoft365ConnectorOptions(10, 4000),
            CreateSearchOptions(),
            new EvidenceNormalizer());
        var request = new SearchMicrosoft365ToolArguments(query, null, null, null);

        // When
        var action = () => connector.SearchAsync(
            request,
            new ConnectorExecutionContext(
                organizationId,
                memberId,
                tenantId,
                entraUserId,
                IdentityProvider.MicrosoftEntraId,
                UserEmail: "user@contoso.com"),
            CancellationToken.None);

        // Then
        await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Equal(0, retrievalClient.CallCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_IncoherentExecutionContext_When_SearchAsync_Then_RejectsBeforeExternalCalls(
        Guid organizationId,
        Guid memberId,
        string query)
    {
        // Given
        var groupResolver = new RecordingMicrosoft365UserGroupResolver();
        var retrievalClient = new RecordingAgenticRetrievalClient();
        var connector = new Microsoft365Connector(
            groupResolver,
            new EmptyMicrosoft365SharePointGroupResolver(),
            retrievalClient,
            new Microsoft365ConnectorOptions(10, 4000),
            CreateSearchOptions(),
            new EvidenceNormalizer());
        var request = new SearchMicrosoft365ToolArguments(query, null, null, null);
        var context = new ConnectorExecutionContext(
            organizationId,
            memberId,
            "tenant-id",
            Guid.NewGuid(),
            null);

        // When
        var action = () => connector.SearchAsync(
            request,
            context,
            CancellationToken.None);

        // Then
        await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Equal(0, groupResolver.CallCount);
        Assert.Equal(0, retrievalClient.CallCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_SharePointGroupResolutionFails_When_SearchAsync_Then_DoesNotSearchAzure(
        Guid organizationId,
        Guid memberId,
        Guid entraUserId,
        string query,
        string userEmail)
    {
        // Given
        var retrievalClient = new RecordingAgenticRetrievalClient();
        var connector = new Microsoft365Connector(
            new RecordingMicrosoft365UserGroupResolver(),
            new FailingMicrosoft365SharePointGroupResolver(),
            retrievalClient,
            new Microsoft365ConnectorOptions(10, 4000),
            CreateSearchOptions(),
            new EvidenceNormalizer());
        var request = new SearchMicrosoft365ToolArguments(query, null, null, null);

        // When
        var action = () => connector.SearchAsync(
            request,
            new ConnectorExecutionContext(
                organizationId,
                memberId,
                Guid.NewGuid().ToString("D"),
                entraUserId,
                IdentityProvider.MicrosoftEntraId,
                UserEmail: userEmail),
            CancellationToken.None);

        // Then
        await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Equal(0, retrievalClient.CallCount);
    }

    private sealed class FailingMicrosoft365UserGroupResolver : IMicrosoft365UserGroupResolver
    {
        public Task<IReadOnlyCollection<string>> ResolveGroupIdsAsync(
            string externalTenantId,
            string entraUserId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Group resolution failed.");
    }

    private static IOptions<AzureAiSearchOptions> CreateSearchOptions() =>
        Options.Create(new AzureAiSearchOptions
        {
            Endpoint = "https://search.example",
            IndexName = "content-index",
            KnowledgeBaseName = "synaptix-m365-knowledge-base",
            KnowledgeSourceName = "synaptix-m365-knowledge-source",
            KnowledgeBaseMaxOutputSizeInTokens = 6000
        });

    private sealed class RecordingAgenticRetrievalClient : IAgenticRetrievalClient
    {
        public int CallCount { get; private set; }

        public Task<AgenticRetrievalResult> RetrieveAsync(
            AgenticRetrievalRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new AgenticRetrievalResult(string.Empty, [], []));
        }
    }

    private sealed class EmptyMicrosoft365SharePointGroupResolver
        : IMicrosoft365SharePointGroupResolver
    {
        public Task<IReadOnlyCollection<string>> ResolveGroupIdsAsync(
            Guid organizationId,
            string externalTenantId,
            string entraUserId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<string>>([]);
    }

    private sealed class FailingMicrosoft365SharePointGroupResolver
        : IMicrosoft365SharePointGroupResolver
    {
        public Task<IReadOnlyCollection<string>> ResolveGroupIdsAsync(
            Guid organizationId,
            string externalTenantId,
            string entraUserId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("SharePoint group resolution failed.");
    }

    private sealed class RecordingMicrosoft365UserGroupResolver : IMicrosoft365UserGroupResolver
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyCollection<string>> ResolveGroupIdsAsync(
            string externalTenantId,
            string entraUserId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyCollection<string>>([]);
        }
    }

}
