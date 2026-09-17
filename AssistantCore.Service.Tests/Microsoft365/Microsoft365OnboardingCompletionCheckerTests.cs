using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Caching.Memory;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365OnboardingCompletionCheckerTests
{
    [Theory, AutoDomainData]
    public async Task Given_NoConnection_When_IsCompleteAsync_Then_ReturnsFalse(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        // Given
        var checker = CreateChecker(organizationId, connection: null, siteIds: []);

        // When
        var isComplete = await checker.IsCompleteAsync(organizationId, cancellationToken);

        // Then
        Assert.False(isComplete);
    }

    [Theory, AutoDomainData]
    public async Task Given_AConnectionPendingConsent_When_IsCompleteAsync_Then_ReturnsFalse(
        Guid organizationId,
        string siteId,
        CancellationToken cancellationToken)
    {
        // Given
        var checker = CreateChecker(
            organizationId,
            new Microsoft365Connection
            {
                OrganizationId = organizationId,
                Status = Microsoft365ConnectionStatus.PendingConsent
            },
            siteIds: [siteId]);

        // When
        var isComplete = await checker.IsCompleteAsync(organizationId, cancellationToken);

        // Then
        Assert.False(isComplete);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnActiveConnectionWithoutAnySelectedSite_When_IsCompleteAsync_Then_ReturnsFalse(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        // Given
        var checker = CreateChecker(
            organizationId,
            new Microsoft365Connection
            {
                OrganizationId = organizationId,
                Status = Microsoft365ConnectionStatus.Active
            },
            siteIds: []);

        // When
        var isComplete = await checker.IsCompleteAsync(organizationId, cancellationToken);

        // Then
        Assert.False(isComplete);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnActiveConnectionWithASelectedSiteButNoIndexedSource_When_IsCompleteAsync_Then_ReturnsFalse(
        Guid organizationId,
        string siteId,
        CancellationToken cancellationToken)
    {
        // Given
        var checker = CreateChecker(
            organizationId,
            new Microsoft365Connection
            {
                OrganizationId = organizationId,
                Status = Microsoft365ConnectionStatus.Active
            },
            siteIds: [siteId],
            hasIndexedSource: false);

        // When
        var isComplete = await checker.IsCompleteAsync(organizationId, cancellationToken);

        // Then
        Assert.False(isComplete);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnActiveConnectionWithAnIndexedSource_When_IsCompleteAsync_Then_ReturnsTrue(
        Guid organizationId,
        string siteId,
        CancellationToken cancellationToken)
    {
        // Given
        var checker = CreateChecker(
            organizationId,
            new Microsoft365Connection
            {
                OrganizationId = organizationId,
                Status = Microsoft365ConnectionStatus.Active
            },
            siteIds: [siteId]);

        // When
        var isComplete = await checker.IsCompleteAsync(organizationId, cancellationToken);

        // Then
        Assert.True(isComplete);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnotherOrganizationId_When_IsCompleteAsync_Then_QueriesOnlyTheRequestedOrganization(
        Guid organizationId,
        Guid anotherOrganizationId,
        string siteId,
        CancellationToken cancellationToken)
    {
        // Given
        var checker = CreateChecker(
            organizationId,
            new Microsoft365Connection
            {
                OrganizationId = organizationId,
                Status = Microsoft365ConnectionStatus.Active
            },
            siteIds: [siteId]);

        // When
        var isComplete = await checker.IsCompleteAsync(anotherOrganizationId, cancellationToken);

        // Then
        Assert.False(isComplete);
    }

    [Theory, AutoDomainData]
    public async Task Given_TwoCallsForTheSameOrganization_When_IsCompleteAsync_Then_QueriesTheRepositoriesOnlyOnce(
        Guid organizationId,
        string siteId,
        CancellationToken cancellationToken)
    {
        // Given
        var connectionRepository = new StubConnectionRepository(
            organizationId,
            new Microsoft365Connection
            {
                OrganizationId = organizationId,
                Status = Microsoft365ConnectionStatus.Active
            });
        var sourceRepository = new StubSourceRepository(organizationId, [siteId]);
        var checker = new Microsoft365OnboardingCompletionChecker(
            connectionRepository,
            sourceRepository,
            new MemoryCache(new MemoryCacheOptions()));

        // When
        var firstResult = await checker.IsCompleteAsync(organizationId, cancellationToken);
        var secondResult = await checker.IsCompleteAsync(organizationId, cancellationToken);

        // Then
        Assert.True(firstResult);
        Assert.True(secondResult);
        Assert.Equal(1, connectionRepository.CallCount);
        Assert.Equal(2, sourceRepository.CallCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_TwoDifferentOrganizations_When_IsCompleteAsync_Then_CachesEachOrganizationSeparately(
        Guid organizationId,
        Guid anotherOrganizationId,
        string siteId,
        CancellationToken cancellationToken)
    {
        // Given
        var connectionRepository = new StubConnectionRepository(
            organizationId,
            new Microsoft365Connection
            {
                OrganizationId = organizationId,
                Status = Microsoft365ConnectionStatus.Active
            });
        var sourceRepository = new StubSourceRepository(organizationId, [siteId]);
        var checker = new Microsoft365OnboardingCompletionChecker(
            connectionRepository,
            sourceRepository,
            new MemoryCache(new MemoryCacheOptions()));

        // When
        var ownResult = await checker.IsCompleteAsync(organizationId, cancellationToken);
        var otherResult = await checker.IsCompleteAsync(anotherOrganizationId, cancellationToken);

        // Then
        Assert.True(ownResult);
        Assert.False(otherResult);
        Assert.Equal(2, connectionRepository.CallCount);
    }

    private static Microsoft365OnboardingCompletionChecker CreateChecker(
        Guid connectionOrganizationId,
        Microsoft365Connection? connection,
        IReadOnlyCollection<string> siteIds,
        bool? hasIndexedSource = null) =>
        new(
            new StubConnectionRepository(connectionOrganizationId, connection),
            new StubSourceRepository(connectionOrganizationId, siteIds, hasIndexedSource),
            new MemoryCache(new MemoryCacheOptions()));

    private sealed class StubConnectionRepository(
        Guid organizationId,
        Microsoft365Connection? connection) : IMicrosoft365ConnectionRepository
    {
        public int CallCount { get; private set; }

        public Task<Microsoft365Connection?> FindByOrganizationAsync(
            Guid requestedOrganizationId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(requestedOrganizationId == organizationId ? connection : null);
        }

        public Task<Microsoft365Connection> PrepareConsentAsync(Guid organizationId, string stateHash, DateTimeOffset stateExpiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) => Task.FromResult(new Microsoft365Connection());
        public Task<Microsoft365Connection?> FindConsentAsync(Guid organizationId, string stateHash, CancellationToken cancellationToken = default) => Task.FromResult<Microsoft365Connection?>(null);
        public Task<bool> IsTenantConnectedToAnotherOrganizationAsync(Guid organizationId, string tenantId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<Microsoft365Connection?> FindByIdAsync(Guid connectionId, Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<Microsoft365Connection?>(null);
        public Task<Microsoft365Connection?> FindForProcessingAsync(Guid connectionId, CancellationToken cancellationToken = default) => Task.FromResult<Microsoft365Connection?>(null);
        public Task CompleteConsentAsync(Microsoft365Connection connection, string tenantId, DateTimeOffset completedAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkConsentErrorAsync(Microsoft365Connection connection, string errorCode, DateTimeOffset occurredAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevokeAsync(Microsoft365Connection connection, DateTimeOffset occurredAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubSourceRepository(
        Guid organizationId,
        IReadOnlyCollection<string> siteIds,
        bool? hasIndexedSource = null) : IMicrosoft365SourceDiscoveryRepository
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyCollection<string>> GetSiteIdsAsync(
            Guid requestedOrganizationId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(requestedOrganizationId == organizationId
                ? siteIds
                : (IReadOnlyCollection<string>)[]);
        }

        public Task<bool> HasIndexedSourceAsync(
            Guid requestedOrganizationId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(
                requestedOrganizationId == organizationId
                && (hasIndexedSource ?? siteIds.Count > 0));
        }

        public Task<bool> IsEnvironmentReadyAsync(
            Guid requestedOrganizationId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(
                requestedOrganizationId == organizationId
                && (hasIndexedSource ?? siteIds.Count > 0));
        }
        public Task<Microsoft365Site?> FindSiteAsync(Guid organizationId, string siteId, CancellationToken cancellationToken = default) => Task.FromResult<Microsoft365Site?>(null);
        public Task<IReadOnlyCollection<Microsoft365List>> GetListsAsync(Guid organizationId, string siteId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<Microsoft365List>>([]);
        public Task<Microsoft365List?> FindListAsync(Guid organizationId, string siteId, string listId, CancellationToken cancellationToken = default) => Task.FromResult<Microsoft365List?>(null);
        public Task<Microsoft365ListIndexingRequestCounts> SaveListActivationAsync(Microsoft365List list, DateTimeOffset requestedAt, CancellationToken cancellationToken = default) => Task.FromResult(Microsoft365ListIndexingRequestCounts.Empty);
        public Task<Microsoft365ListIndexingRequestCounts> SaveListDeactivationAsync(Microsoft365List list, DateTimeOffset requestedAt, bool requestIndexCleanup, CancellationToken cancellationToken = default) => Task.FromResult(Microsoft365ListIndexingRequestCounts.Empty);
        public Task ReconcileSiteSourcesAsync(Microsoft365Site site, IReadOnlyCollection<Microsoft365SourceDiscoveryData> drives, IReadOnlyCollection<Microsoft365SourceDiscoveryData> lists, DateTimeOffset discoveredAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
