using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365SubscriptionMaintenanceServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-21T12:00:00Z");

    [Theory, AutoDomainData]
    public async Task Given_APendingSubscription_When_RunMaintenanceAsync_Then_GraphSubscriptionIsCreated(
        Guid organizationId,
        string subscriptionId,
        string tenantId,
        string siteId,
        string listId)
    {
        // Given
        var subscription = CreateSubscription(
            organizationId,
            subscriptionId,
            tenantId,
            siteId,
            listId);
        subscription.Status = Microsoft365SubscriptionStatus.Pending;
        subscription.MicrosoftSubscriptionId = null;
        subscription.ProtectedClientState = null;
        subscription.ExpiresAt = null;
        var client = new SubscriptionClientFake
        {
            CreationResult = new Microsoft365SubscriptionResult(
                subscriptionId,
                subscription.Resource,
                Now.AddHours(48))
        };
        var service = CreateService(
            new SubscriptionRepositoryFake(subscription),
            client,
            new SynchronizationPublisherFake());

        // When
        await service.RunMaintenanceAsync(CancellationToken.None);

        // Then
        Assert.Equal(1, client.CreateCount);
        Assert.Equal(subscriptionId, subscription.MicrosoftSubscriptionId);
        Assert.Equal(Microsoft365SubscriptionStatus.Active, subscription.Status);
        Assert.Equal("protected-client-state", subscription.ProtectedClientState);
    }

    [Theory, AutoDomainData]
    public async Task Given_ASubscriptionExpiringSoon_When_RunMaintenanceAsync_Then_ExpirationIsRenewed(
        Guid organizationId,
        string subscriptionId,
        string tenantId,
        string siteId,
        string listId)
    {
        // Given
        var subscription = CreateSubscription(
            organizationId,
            subscriptionId,
            tenantId,
            siteId,
            listId);
        var renewedAt = Now.AddHours(48);
        var client = new SubscriptionClientFake
        {
            RenewalResult = new Microsoft365SubscriptionRenewalResult(
                true,
                new Microsoft365SubscriptionResult(
                    subscriptionId,
                    subscription.Resource,
                    renewedAt))
        };
        var repository = new SubscriptionRepositoryFake(subscription);
        var service = CreateService(repository, client, new SynchronizationPublisherFake());

        // When
        await service.RunMaintenanceAsync(CancellationToken.None);

        // Then
        Assert.Equal(1, client.RenewCount);
        Assert.Equal(renewedAt, subscription.ExpiresAt);
        Assert.Equal(Microsoft365SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(Now, subscription.LastRenewedAt);
        Assert.Null(subscription.LastErrorCode);
    }

    [Theory, AutoDomainData]
    public async Task Given_MicrosoftReturnsNotFound_When_RunMaintenanceAsync_Then_SubscriptionIsRecreatedAndReconciliationIsPublished(
        Guid organizationId,
        string oldSubscriptionId,
        string newSubscriptionId,
        string tenantId,
        string siteId,
        string listId)
    {
        // Given
        var subscription = CreateSubscription(
            organizationId,
            oldSubscriptionId,
            tenantId,
            siteId,
            listId);
        var client = new SubscriptionClientFake
        {
            RenewalResult = Microsoft365SubscriptionRenewalResult.NotFound,
            CreationResult = new Microsoft365SubscriptionResult(
                newSubscriptionId,
                subscription.Resource,
                Now.AddHours(48))
        };
        var repository = new SubscriptionRepositoryFake(subscription);
        var publisher = new SynchronizationPublisherFake();
        var service = CreateService(repository, client, publisher);

        // When
        await service.RunMaintenanceAsync(CancellationToken.None);

        // Then
        Assert.Equal(1, client.RenewCount);
        Assert.Equal(1, client.CreateCount);
        Assert.Equal(newSubscriptionId, subscription.MicrosoftSubscriptionId);
        Assert.Equal(Microsoft365SubscriptionStatus.Active, subscription.Status);
        var work = Assert.Single(publisher.Published);
        Assert.Equal("SynchronizeList", work.WorkType);
        Assert.Equal(newSubscriptionId, work.SubscriptionId);
        Assert.DoesNotContain(oldSubscriptionId, work.SubscriptionId, StringComparison.Ordinal);
    }

    [Theory, AutoDomainData]
    public async Task Given_AClientStateMigrationIsRequired_When_RunMaintenanceAsync_Then_OldSubscriptionIsDeletedAndRecreatedWithReconciliation(
        Guid organizationId,
        string oldSubscriptionId,
        string newSubscriptionId,
        string tenantId,
        string siteId,
        string listId)
    {
        // Given
        var subscription = CreateSubscription(
            organizationId,
            oldSubscriptionId,
            tenantId,
            siteId,
            listId);
        subscription.Status = Microsoft365SubscriptionStatus.RenewalRequired;
        subscription.LastErrorCode = "ClientStateMigrationRequired";
        subscription.ProtectedClientState = "legacy-sha256-hash";
        var client = new SubscriptionClientFake
        {
            CreationResult = new Microsoft365SubscriptionResult(
                newSubscriptionId,
                subscription.Resource,
                Now.AddHours(48))
        };
        var repository = new SubscriptionRepositoryFake(subscription);
        var publisher = new SynchronizationPublisherFake();
        var service = CreateService(repository, client, publisher);

        // When
        await service.RunMaintenanceAsync(CancellationToken.None);

        // Then
        Assert.Equal(1, client.DeleteCount);
        Assert.Equal(1, client.CreateCount);
        Assert.Equal(newSubscriptionId, subscription.MicrosoftSubscriptionId);
        Assert.Equal("protected-client-state", subscription.ProtectedClientState);
        Assert.NotEqual("legacy-sha256-hash", subscription.ProtectedClientState);
        Assert.Equal(Microsoft365SubscriptionStatus.Active, subscription.Status);
        Assert.Null(subscription.LastErrorCode);
        var work = Assert.Single(publisher.Published);
        Assert.Equal(newSubscriptionId, work.SubscriptionId);
    }

    [Theory, AutoDomainData]
    public async Task Given_AClientStateMigrationOnASubscriptionWithoutAGraphId_When_RunMaintenanceAsync_Then_NothingIsDeletedButANewOneIsCreated(
        Guid organizationId,
        string newSubscriptionId,
        string tenantId,
        string siteId,
        string listId)
    {
        // Given
        var subscription = CreateSubscription(
            organizationId,
            "unused-subscription-id",
            tenantId,
            siteId,
            listId);
        subscription.Status = Microsoft365SubscriptionStatus.RenewalRequired;
        subscription.LastErrorCode = "ClientStateMigrationRequired";
        subscription.MicrosoftSubscriptionId = null;
        var client = new SubscriptionClientFake
        {
            CreationResult = new Microsoft365SubscriptionResult(
                newSubscriptionId,
                subscription.Resource,
                Now.AddHours(48))
        };
        var service = CreateService(
            new SubscriptionRepositoryFake(subscription),
            client,
            new SynchronizationPublisherFake());

        // When
        await service.RunMaintenanceAsync(CancellationToken.None);

        // Then
        Assert.Equal(0, client.DeleteCount);
        Assert.Equal(1, client.CreateCount);
        Assert.Equal(newSubscriptionId, subscription.MicrosoftSubscriptionId);
    }

    [Theory, AutoDomainData]
    public async Task Given_ARevocationRequest_When_RunMaintenanceAsync_Then_SubscriptionIsDeletedAndMarkedRevoked(
        Guid organizationId,
        string subscriptionId,
        string tenantId,
        string siteId,
        string listId)
    {
        // Given
        var subscription = CreateSubscription(
            organizationId,
            subscriptionId,
            tenantId,
            siteId,
            listId);
        subscription.Status = Microsoft365SubscriptionStatus.RevocationRequired;
        subscription.Microsoft365Source.Status = Microsoft365SourceStatus.Disabled;
        subscription.Microsoft365Source.IsIndexed = false;
        var client = new SubscriptionClientFake();
        var service = CreateService(
            new SubscriptionRepositoryFake(subscription),
            client,
            new SynchronizationPublisherFake());

        // When
        await service.RunMaintenanceAsync(CancellationToken.None);

        // Then
        Assert.Equal(1, client.DeleteCount);
        Assert.Equal(Microsoft365SubscriptionStatus.Revoked, subscription.Status);
        Assert.Null(subscription.ProtectedClientState);
        Assert.Null(subscription.ExpiresAt);
    }

    private static Microsoft365SubscriptionMaintenanceService CreateService(
        SubscriptionRepositoryFake repository,
        SubscriptionClientFake client,
        SynchronizationPublisherFake publisher) =>
        new(
            repository,
            client,
            new ClientStateProtectorFake(),
            publisher,
            Options.Create(new Microsoft365Options
            {
                WebhookBaseUrl = "https://assistant.example",
                SubscriptionLifetimeHours = 48,
                SubscriptionRenewalLeadTimeHours = 24
            }),
            new FixedTimeProvider(),
            NullLogger<Microsoft365SubscriptionMaintenanceService>.Instance);

    private static Microsoft365Subscription CreateSubscription(
        Guid organizationId,
        string subscriptionId,
        string tenantId,
        string siteId,
        string listId)
    {
        var connector = new OrganizationConnector
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Status = RecordStatus.Active,
            Type = ConnectorType.Microsoft365
        };
        var connection = new Microsoft365Connection
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            OrganizationConnectorId = connector.Id,
            TenantId = tenantId,
            Status = Microsoft365ConnectionStatus.Active,
            OrganizationConnector = connector
        };
        var source = new Microsoft365List
        {
            Id = Guid.NewGuid(),
            Microsoft365ConnectionId = connection.Id,
            Microsoft365Connection = connection,
            OrganizationId = organizationId,
            SiteId = siteId,
            ListId = listId,
            Kind = Microsoft365SourceKind.SharePointList,
            Status = Microsoft365SourceStatus.Enabled,
            IsIndexed = true
        };
        return new Microsoft365Subscription
        {
            Id = Guid.NewGuid(),
            Microsoft365SourceId = source.Id,
            Microsoft365Source = source,
            OrganizationId = organizationId,
            MicrosoftSubscriptionId = subscriptionId,
            Resource = $"/sites/{siteId}/lists/{listId}",
            ProtectedClientState = "protected-client-state",
            ExpiresAt = Now.AddHours(1),
            Status = Microsoft365SubscriptionStatus.Active,
            CreatedAt = Now.AddDays(-1),
            UpdatedAt = Now.AddDays(-1)
        };
    }

    private sealed class SubscriptionRepositoryFake(Microsoft365Subscription subscription)
        : IMicrosoft365SubscriptionRepository
    {
        public Task<IReadOnlyCollection<Microsoft365Subscription>> GetMaintenanceCandidatesAsync(
            DateTimeOffset renewBefore,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<Microsoft365Subscription>>([subscription]);

        public Task<IReadOnlyCollection<Microsoft365Subscription>> GetReconciliationCandidatesAsync(
            DateTimeOffset dueAt,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<Microsoft365Subscription>>([]);

        public Task<Microsoft365Subscription?> FindActiveForNotificationAsync(
            string microsoftSubscriptionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Microsoft365Subscription?>(null);

        public Microsoft365Synchronization GetOrCreateDeltaSynchronization(
            Microsoft365Subscription candidate,
            DateTimeOffset requestedAt)
        {
            var existing = candidate.Microsoft365Source.Synchronizations.SingleOrDefault();
            if (existing is not null)
            {
                return existing;
            }

            var created = new Microsoft365Synchronization
            {
                Id = Guid.NewGuid(),
                Microsoft365SourceId = candidate.Microsoft365SourceId,
                Type = Microsoft365SynchronizationType.Delta,
                Status = Microsoft365SynchronizationStatus.Pending,
                RequestedAt = requestedAt
            };
            candidate.Microsoft365Source.Synchronizations.Add(created);
            return created;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SubscriptionClientFake : IMicrosoft365SubscriptionClient
    {
        public Microsoft365SubscriptionRenewalResult RenewalResult { get; init; } =
            Microsoft365SubscriptionRenewalResult.NotFound;

        public Microsoft365SubscriptionResult? CreationResult { get; init; }

        public int CreateCount { get; private set; }

        public int RenewCount { get; private set; }

        public int DeleteCount { get; private set; }

        public Task<Microsoft365SubscriptionResult> CreateAsync(
            string tenantId,
            string resource,
            string notificationUrl,
            DateTimeOffset expiresAt,
            string clientState,
            string changeType,
            CancellationToken cancellationToken = default)
        {
            CreateCount++;
            return Task.FromResult(CreationResult
                ?? new Microsoft365SubscriptionResult(Guid.NewGuid().ToString(), resource, expiresAt));
        }

        public Task<Microsoft365SubscriptionRenewalResult> RenewAsync(
            string tenantId,
            string subscriptionId,
            string notificationUrl,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default)
        {
            RenewCount++;
            return Task.FromResult(RenewalResult);
        }

        public Task DeleteAsync(
            string tenantId,
            string subscriptionId,
            CancellationToken cancellationToken = default)
        {
            DeleteCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class ClientStateProtectorFake : IMicrosoft365ClientStateProtector
    {
        public Microsoft365ClientState Create() =>
            new("raw-client-state", "protected-client-state");

        public bool Matches(string clientState, string protectedClientState) => false;
    }

    private sealed class SynchronizationPublisherFake : IMicrosoft365SynchronizationPublisher
    {
        public List<Microsoft365SynchronizationWork> Published { get; } = [];

        public Task PublishAsync(
            Microsoft365SynchronizationWork work,
            CancellationToken cancellationToken = default)
        {
            Published.Add(work);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
