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
        var checker = CreateChecker(organizationId, connection: null);

        // When
        var isComplete = await checker.IsCompleteAsync(organizationId, cancellationToken);

        // Then
        Assert.False(isComplete);
    }

    [Theory, AutoDomainData]
    public async Task Given_AConnectionPendingConsentWithOldCompletionMarker_When_IsCompleteAsync_Then_ReturnsFalse(
        Guid organizationId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        // Given
        var checker = CreateChecker(
            organizationId,
            new Microsoft365Connection
            {
                OrganizationId = organizationId,
                Status = Microsoft365ConnectionStatus.PendingConsent,
                OnboardingCompletedAt = completedAt
            });

        // When
        var isComplete = await checker.IsCompleteAsync(organizationId, cancellationToken);

        // Then
        Assert.False(isComplete);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnActiveConnectionWithoutPersistedCompletion_When_IsCompleteAsync_Then_ReturnsFalse(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        // Given
        var checker = CreateChecker(
            organizationId,
            new Microsoft365Connection
            {
                OrganizationId = organizationId,
                Status = Microsoft365ConnectionStatus.Active,
                OnboardingCompletedAt = null
            });

        // When
        var isComplete = await checker.IsCompleteAsync(organizationId, cancellationToken);

        // Then
        Assert.False(isComplete);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnActivePreviouslyCompletedConnection_When_IsCompleteAsync_Then_ReturnsTrueRegardlessOfCurrentSources(
        Guid organizationId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        // Given
        var checker = CreateChecker(
            organizationId,
            new Microsoft365Connection
            {
                OrganizationId = organizationId,
                Status = Microsoft365ConnectionStatus.Active,
                OnboardingCompletedAt = completedAt
            });

        // When
        var isComplete = await checker.IsCompleteAsync(organizationId, cancellationToken);

        // Then
        Assert.True(isComplete);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnotherOrganizationId_When_IsCompleteAsync_Then_QueriesOnlyTheRequestedOrganization(
        Guid organizationId,
        Guid anotherOrganizationId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        // Given
        var checker = CreateChecker(
            organizationId,
            new Microsoft365Connection
            {
                OrganizationId = organizationId,
                Status = Microsoft365ConnectionStatus.Active,
                OnboardingCompletedAt = completedAt
            });

        // When
        var isComplete = await checker.IsCompleteAsync(anotherOrganizationId, cancellationToken);

        // Then
        Assert.False(isComplete);
    }

    [Theory, AutoDomainData]
    public async Task Given_TwoCallsForTheSameOrganization_When_IsCompleteAsync_Then_QueriesTheRepositoryOnlyOnce(
        Guid organizationId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        // Given
        var connectionRepository = new StubConnectionRepository(
            organizationId,
            new Microsoft365Connection
            {
                OrganizationId = organizationId,
                Status = Microsoft365ConnectionStatus.Active,
                OnboardingCompletedAt = completedAt
            });
        var checker = new Microsoft365OnboardingCompletionChecker(
            connectionRepository,
            new MemoryCache(new MemoryCacheOptions()));

        // When
        var firstResult = await checker.IsCompleteAsync(organizationId, cancellationToken);
        var secondResult = await checker.IsCompleteAsync(organizationId, cancellationToken);

        // Then
        Assert.True(firstResult);
        Assert.True(secondResult);
        Assert.Equal(1, connectionRepository.CallCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_TwoDifferentOrganizations_When_IsCompleteAsync_Then_CachesEachOrganizationSeparately(
        Guid organizationId,
        Guid anotherOrganizationId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        // Given
        var connectionRepository = new StubConnectionRepository(
            organizationId,
            new Microsoft365Connection
            {
                OrganizationId = organizationId,
                Status = Microsoft365ConnectionStatus.Active,
                OnboardingCompletedAt = completedAt
            });
        var checker = new Microsoft365OnboardingCompletionChecker(
            connectionRepository,
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
        Microsoft365Connection? connection) =>
        new(
            new StubConnectionRepository(connectionOrganizationId, connection),
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
}
