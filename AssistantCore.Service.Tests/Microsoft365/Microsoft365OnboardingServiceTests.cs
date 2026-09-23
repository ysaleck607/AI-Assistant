using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Services.AuthenticateUser;
using AssistantCore.Service.Application.Services.Microsoft365;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365OnboardingServiceTests
{
    [Theory, AutoDomainData]
    public async Task Given_NoConnection_When_GetStatusAsync_Then_ReturnsFirstStepForAdministrator(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        // Given
        var service = CreateService(
            organizationId,
            OrganizationRole.Admin,
            connection: null,
            siteIds: [],
            hasIndexedSource: false);

        // When
        var status = await service.GetStatusAsync(cancellationToken);

        // Then
        Assert.True(status.IsAdministrator);
        Assert.Equal("NotStarted", status.ConnectionStatus);
        Assert.False(status.IsConsentComplete);
        Assert.False(status.HasSelectedSite);
        Assert.False(status.HasIndexedSource);
        Assert.False(status.IsComplete);
    }

    [Theory, AutoDomainData]
    public async Task Given_ActiveConnectionAndFirstSelectedSite_When_GetStatusAsync_Then_PersistsCompletedOnboarding(
        Guid organizationId,
        string siteId,
        CancellationToken cancellationToken)
    {
        // Given
        var repository = new StubConnectionRepository(new Microsoft365Connection
        {
            OrganizationId = organizationId,
            Status = Microsoft365ConnectionStatus.Active
        });
        var service = CreateService(
            organizationId,
            OrganizationRole.Admin,
            repository,
            [siteId],
            hasIndexedSource: false);

        // When
        var status = await service.GetStatusAsync(cancellationToken);

        // Then
        Assert.True(status.IsConsentComplete);
        Assert.True(status.HasSelectedSite);
        Assert.False(status.HasIndexedSource);
        Assert.True(status.HasCompletedInitialSetup);
        Assert.True(status.IsComplete);
        Assert.Equal(1, repository.CompleteOnboardingCallCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_PreviouslyCompletedSetupWithoutCurrentSites_When_GetStatusAsync_Then_RemainsCompleted(
        Guid organizationId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        // Given
        var service = CreateService(
            organizationId,
            OrganizationRole.User,
            new Microsoft365Connection
            {
                OrganizationId = organizationId,
                Status = Microsoft365ConnectionStatus.Active,
                OnboardingCompletedAt = completedAt
            },
            [],
            hasIndexedSource: false);

        // When
        var status = await service.GetStatusAsync(cancellationToken);

        // Then
        Assert.False(status.HasSelectedSite);
        Assert.False(status.HasIndexedSource);
        Assert.True(status.HasCompletedInitialSetup);
        Assert.True(status.IsComplete);
    }

    [Theory, AutoDomainData]
    public async Task Given_ActiveConnectionAndIndexedSource_When_GetStatusAsync_Then_ReturnsCompletedOnboarding(
        Guid organizationId,
        string siteId,
        CancellationToken cancellationToken)
    {
        // Given
        var service = CreateService(
            organizationId,
            OrganizationRole.User,
            new Microsoft365Connection
            {
                OrganizationId = organizationId,
                Status = Microsoft365ConnectionStatus.Active
            },
            [siteId],
            hasIndexedSource: true);

        // When
        var status = await service.GetStatusAsync(cancellationToken);

        // Then
        Assert.False(status.IsAdministrator);
        Assert.True(status.IsConsentComplete);
        Assert.True(status.HasSelectedSite);
        Assert.True(status.HasIndexedSource);
        Assert.True(status.IsComplete);
    }

    private static Microsoft365OnboardingService CreateService(
        Guid organizationId,
        OrganizationRole role,
        Microsoft365Connection? connection,
        IReadOnlyCollection<string> siteIds,
        bool hasIndexedSource) =>
        CreateService(
            organizationId,
            role,
            new StubConnectionRepository(connection),
            siteIds,
            hasIndexedSource);

    private static Microsoft365OnboardingService CreateService(
        Guid organizationId,
        OrganizationRole role,
        StubConnectionRepository repository,
        IReadOnlyCollection<string> siteIds,
        bool hasIndexedSource) =>
        new(
            new StubAuthenticateUserService(organizationId, role),
            repository,
            new StubSourceRepository(siteIds, hasIndexedSource),
            TimeProvider.System);

    private sealed class StubAuthenticateUserService(
        Guid organizationId,
        OrganizationRole role) : IAuthenticateUserService
    {
        public Task<(Organization Organization, OrganizationMember Member)> GetOrganizationAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult((
                new Organization { Id = organizationId },
                new OrganizationMember
                {
                    OrganizationId = organizationId,
                    Role = role
                }));
    }

    private sealed class StubConnectionRepository(Microsoft365Connection? connection)
        : IMicrosoft365ConnectionRepository
    {
        public int CompleteOnboardingCallCount { get; private set; }

        public Task<Microsoft365Connection?> FindByOrganizationAsync(
            Guid organizationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(connection);

        public Task CompleteOnboardingAsync(
            Guid organizationId,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default)
        {
            CompleteOnboardingCallCount++;
            if (connection is not null)
            {
                connection.OnboardingCompletedAt = completedAt;
            }
            return Task.CompletedTask;
        }

        public Task<Microsoft365Connection> PrepareConsentAsync(Guid organizationId, string stateHash, DateTimeOffset stateExpiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Microsoft365Connection());
        public Task<Microsoft365Connection?> FindConsentAsync(Guid organizationId, string stateHash, CancellationToken cancellationToken = default) => Task.FromResult<Microsoft365Connection?>(null);
        public Task<bool> IsTenantConnectedToAnotherOrganizationAsync(Guid organizationId, string tenantId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<Microsoft365Connection?> FindByIdAsync(Guid connectionId, Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<Microsoft365Connection?>(null);
        public Task<Microsoft365Connection?> FindForProcessingAsync(Guid connectionId, CancellationToken cancellationToken = default) => Task.FromResult<Microsoft365Connection?>(null);
        public Task CompleteConsentAsync(Microsoft365Connection connection, string tenantId, DateTimeOffset completedAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkConsentErrorAsync(Microsoft365Connection connection, string errorCode, DateTimeOffset occurredAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevokeAsync(Microsoft365Connection connection, DateTimeOffset occurredAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubSourceRepository(
        IReadOnlyCollection<string> siteIds,
        bool hasIndexedSource) : IMicrosoft365SourceDiscoveryRepository
    {
        public Task<IReadOnlyCollection<string>> GetSiteIdsAsync(
            Guid organizationId,
            CancellationToken cancellationToken = default) => Task.FromResult(siteIds);

        public Task<bool> HasIndexedSourceAsync(
            Guid organizationId,
            CancellationToken cancellationToken = default) => Task.FromResult(hasIndexedSource);

        public Task<Microsoft365Site?> FindSiteAsync(Guid organizationId, string siteId, CancellationToken cancellationToken = default) => Task.FromResult<Microsoft365Site?>(null);
        public Task<IReadOnlyCollection<Microsoft365List>> GetListsAsync(Guid organizationId, string siteId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<Microsoft365List>>([]);
        public Task<Microsoft365List?> FindListAsync(Guid organizationId, string siteId, string listId, CancellationToken cancellationToken = default) => Task.FromResult<Microsoft365List?>(null);
        public Task<Microsoft365ListIndexingRequestCounts> SaveListActivationAsync(Microsoft365List list, DateTimeOffset requestedAt, CancellationToken cancellationToken = default) => Task.FromResult(Microsoft365ListIndexingRequestCounts.Empty);
        public Task<Microsoft365ListIndexingRequestCounts> SaveListDeactivationAsync(Microsoft365List list, DateTimeOffset requestedAt, bool requestIndexCleanup, CancellationToken cancellationToken = default) => Task.FromResult(Microsoft365ListIndexingRequestCounts.Empty);
        public Task ReconcileSiteSourcesAsync(Microsoft365Site site, IReadOnlyCollection<Microsoft365SourceDiscoveryData> drives, IReadOnlyCollection<Microsoft365SourceDiscoveryData> lists, DateTimeOffset discoveredAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
