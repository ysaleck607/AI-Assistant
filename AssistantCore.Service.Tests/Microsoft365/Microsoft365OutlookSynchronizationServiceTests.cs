using System.Runtime.CompilerServices;
using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365OutlookSynchronizationServiceTests
{
    [Theory, AutoDomainData]
    public async Task Given_CreatedModifiedDeletedAndEmptyMessages_When_StartInitialSynchronizationAsync_Then_IndexesAndDeletesWithExpectedCounters(
        Guid sourceId,
        Guid synchronizationId,
        Guid organizationId,
        DateTimeOffset now)
    {
        const string deltaLink = "https://graph.microsoft.com/v1.0/users/user/mailFolders/inbox/messages/delta?$deltatoken=next";
        var source = CreateSource(sourceId, organizationId);
        var repository = new RecordingRepository(source);
        var createdAt = now.AddHours(-2);
        var pages = new[]
        {
            new Microsoft365OutlookMessageDeltaPage(
                [
                    CreateMessage("created", "created body", createdAt, createdAt),
                    CreateMessage("modified", "modified body", createdAt, now),
                    CreateMessage("deleted", null, null, null, isDeleted: true),
                    CreateMessage("empty", "   ", createdAt, now)
                ],
                deltaLink)
        };
        var indexing = new RecordingIndexingService();
        var deltaClient = new StubDeltaClient(pages);
        var service = CreateService(repository, deltaClient, indexing, now);

        await service.StartInitialSynchronizationAsync(sourceId, synchronizationId, CancellationToken.None);

        Assert.Equal(2, indexing.IndexedMessages.Count);
        Assert.Contains(indexing.IndexedMessages, item => item.Message.Id == "created");
        Assert.Contains(indexing.IndexedMessages, item => item.Message.Id == "modified");
        Assert.Equal(["deleted", "empty"], indexing.DeletedMessageIds);
        Assert.Equal(organizationId, indexing.IndexedMessages[0].OrganizationId);
        Assert.Equal(sourceId, indexing.IndexedMessages[0].SourceId);
        Assert.Equal("user@contoso.com", indexing.IndexedMessages[0].MailboxUserId);
        Assert.Equal(now.AddDays(-180), deltaClient.ReceivedSince);
        Assert.Equal(deltaLink, repository.ConfirmedDeltaLink);
        Assert.Equal(Microsoft365SynchronizationStatus.Succeeded, repository.RecordedStatus);
        Assert.Equal(1, repository.RecordedCounters?.CreatedCount);
        Assert.Equal(1, repository.RecordedCounters?.ModifiedCount);
        Assert.Equal(1, repository.RecordedCounters?.DeletedCount);
        Assert.Equal(1, repository.RecordedCounters?.IgnoredCount);
        Assert.Equal(0, repository.RecordedCounters?.FailedCount);
        Assert.Equal(1, repository.ReleaseCount);
        Assert.Null(repository.ReleasedErrorCode);
    }

    [Theory, AutoDomainData]
    public async Task Given_ExpiredDeltaCheckpoint_When_StartDeltaSynchronizationAsync_Then_MarksFullResyncAndRunsInitialSynchronization(
        Guid sourceId,
        Guid synchronizationId,
        Guid organizationId,
        DateTimeOffset now)
    {
        const string storedDeltaLink = "https://graph.microsoft.com/v1.0/users/user/mailFolders/inbox/messages/delta?$deltatoken=expired";
        const string freshDeltaLink = "https://graph.microsoft.com/v1.0/users/user/mailFolders/inbox/messages/delta?$deltatoken=fresh";
        var source = CreateSource(sourceId, organizationId);
        source.DeltaLink = storedDeltaLink;
        var repository = new RecordingRepository(source);
        var deltaClient = new InvalidCheckpointThenInitialClient(
            [new Microsoft365OutlookMessageDeltaPage([], freshDeltaLink)]);
        var service = CreateService(repository, deltaClient, new RecordingIndexingService(), now);

        await service.StartDeltaSynchronizationAsync(sourceId, synchronizationId, CancellationToken.None);

        Assert.Equal(storedDeltaLink, deltaClient.ReceivedDeltaLink);
        Assert.Equal(1, deltaClient.InitialCallCount);
        Assert.Equal(Microsoft365SourceStatus.FullResyncRequired, repository.MarkedStatus);
        Assert.Equal("MicrosoftGraphOutlookDeltaCheckpointInvalid", repository.FullResyncRequiredErrorCode);
        Assert.Equal(freshDeltaLink, repository.ConfirmedDeltaLink);
        Assert.Equal(Microsoft365SynchronizationStatus.Succeeded, repository.RecordedStatus);
        Assert.Null(repository.ReleasedErrorCode);
    }

    [Theory, AutoDomainData]
    public async Task Given_OutlookSourceIsNotConfigured_When_StartInitialSynchronizationAsync_Then_RejectsBeforeAcquiringLease(
        Guid sourceId,
        Guid synchronizationId,
        Guid organizationId,
        DateTimeOffset now)
    {
        var source = CreateSource(sourceId, organizationId);
        source.Microsoft365Connection.OrganizationConnector.IsConfigured = false;
        var repository = new RecordingRepository(source);
        var deltaClient = new StubDeltaClient([]);
        var service = CreateService(repository, deltaClient, new RecordingIndexingService(), now);

        var action = () => service.StartInitialSynchronizationAsync(
            sourceId,
            synchronizationId,
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Equal(0, repository.LeaseAttemptCount);
        Assert.Equal(0, deltaClient.InitialCallCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnActiveMessageOlderThanRetention_When_StartInitialSynchronizationAsync_Then_DeletesItWithoutIndexing(
        Guid sourceId,
        Guid synchronizationId,
        Guid organizationId,
        DateTimeOffset now)
    {
        const string deltaLink = "https://graph.microsoft.com/v1.0/users/user/mailFolders/inbox/messages/delta?$deltatoken=next";
        var source = CreateSource(sourceId, organizationId);
        var repository = new RecordingRepository(source);
        var staleReceivedAt = now.AddDays(-181);
        var page = new Microsoft365OutlookMessageDeltaPage(
            [CreateMessage("stale", "stale body", staleReceivedAt, staleReceivedAt)],
            deltaLink);
        var indexing = new RecordingIndexingService();
        var service = CreateService(repository, new StubDeltaClient([page]), indexing, now);

        await service.StartInitialSynchronizationAsync(sourceId, synchronizationId, CancellationToken.None);

        Assert.Empty(indexing.IndexedMessages);
        Assert.Equal(["stale"], indexing.DeletedMessageIds);
        Assert.Equal(0, repository.RecordedCounters?.CreatedCount);
        Assert.Equal(0, repository.RecordedCounters?.ModifiedCount);
        Assert.Equal(0, repository.RecordedCounters?.DeletedCount);
        Assert.Equal(1, repository.RecordedCounters?.IgnoredCount);
    }

    private static Microsoft365OutlookSynchronizationService CreateService(
        RecordingRepository repository,
        IMicrosoft365OutlookMessageDeltaClient deltaClient,
        IMicrosoft365OutlookMessageIndexingService indexingService,
        DateTimeOffset now) =>
        new(
            repository,
            repository,
            deltaClient,
            indexingService,
            Options.Create(new Microsoft365Options { SynchronizationLeaseMinutes = 15 }),
            new FixedTimeProvider(now));

    private static Microsoft365Source CreateSource(Guid sourceId, Guid organizationId)
    {
        var organization = new Organization
        {
            Id = organizationId,
            Name = "Contoso",
            Status = RecordStatus.Active
        };
        var connector = new OrganizationConnector
        {
            OrganizationId = organizationId,
            Organization = organization,
            Status = RecordStatus.Active,
            IsConfigured = true
        };
        var connection = new Microsoft365Connection
        {
            OrganizationId = organizationId,
            TenantId = "tenant-id",
            Status = Microsoft365ConnectionStatus.Active,
            Organization = organization,
            OrganizationConnector = connector
        };
        connector.Microsoft365Connection = connection;

        return new Microsoft365Source
        {
            Id = sourceId,
            Kind = Microsoft365SourceKind.OutlookMailbox,
            ExternalResourceId = "user@contoso.com",
            ParentExternalResourceId = "inbox",
            DisplayName = "Inbox",
            IsIndexed = true,
            Status = Microsoft365SourceStatus.Enabled,
            Microsoft365Connection = connection
        };
    }

    private static Microsoft365OutlookMessageDelta CreateMessage(
        string id,
        string? body,
        DateTimeOffset? createdAt,
        DateTimeOffset? modifiedAt,
        bool isDeleted = false) =>
        new(
            id,
            $"Subject {id}",
            body,
            $"https://outlook.office.com/mail/{id}",
            createdAt,
            modifiedAt,
            modifiedAt,
            isDeleted);

    private sealed class RecordingRepository(Microsoft365Source source)
        : IMicrosoft365OutlookSynchronizationRepository, IMicrosoft365SourceSynchronizationRepository
    {
        public int LeaseAttemptCount { get; private set; }
        public int ReleaseCount { get; private set; }
        public string? ReleasedErrorCode { get; private set; }
        public string? ConfirmedDeltaLink { get; private set; }
        public Microsoft365SourceStatus? MarkedStatus { get; private set; }
        public string? FullResyncRequiredErrorCode { get; private set; }
        public Microsoft365SynchronizationStatus? RecordedStatus { get; private set; }
        public Microsoft365SynchronizationCounters? RecordedCounters { get; private set; }

        public Task<Microsoft365Source?> FindForSynchronizationAsync(
            Guid sourceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Microsoft365Source?>(source.Id == sourceId ? source : null);

        public Task<bool> TryAcquireLeaseAsync(
            Guid sourceId,
            Guid leaseId,
            DateTimeOffset attemptedAt,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default)
        {
            LeaseAttemptCount++;
            return Task.FromResult(true);
        }

        public Task<bool> ConfirmCheckpointAsync(
            Guid sourceId,
            Guid leaseId,
            string deltaLink,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default)
        {
            ConfirmedDeltaLink = deltaLink;
            source.DeltaLink = deltaLink;
            if (source.Status == Microsoft365SourceStatus.FullResyncRequired)
            {
                source.Status = Microsoft365SourceStatus.Enabled;
            }
            return Task.FromResult(true);
        }

        public Task<bool> MarkFullResyncRequiredAsync(
            Guid sourceId,
            Guid leaseId,
            string lastErrorCode,
            CancellationToken cancellationToken = default)
        {
            source.Status = Microsoft365SourceStatus.FullResyncRequired;
            MarkedStatus = source.Status;
            FullResyncRequiredErrorCode = lastErrorCode;
            return Task.FromResult(true);
        }

        public Task<bool> MarkAccessErrorAsync(
            Guid sourceId,
            Guid leaseId,
            string lastErrorCode,
            CancellationToken cancellationToken = default)
        {
            source.Status = Microsoft365SourceStatus.Error;
            return Task.FromResult(true);
        }

        public Task<bool> RecordSynchronizationOutcomeAsync(
            Guid sourceId,
            Guid synchronizationId,
            Microsoft365SynchronizationStatus status,
            Microsoft365SynchronizationCounters counters,
            DateTimeOffset completedAt,
            string? lastErrorCode,
            CancellationToken cancellationToken = default)
        {
            RecordedStatus = status;
            RecordedCounters = counters;
            return Task.FromResult(true);
        }

        public Task ReleaseLeaseAsync(
            Guid sourceId,
            Guid leaseId,
            string? lastErrorCode,
            CancellationToken cancellationToken = default)
        {
            ReleaseCount++;
            ReleasedErrorCode = lastErrorCode;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingIndexingService : IMicrosoft365OutlookMessageIndexingService
    {
        public List<(Guid OrganizationId, Guid SourceId, string TenantId, string MailboxUserId, Microsoft365OutlookMessageDelta Message)> IndexedMessages { get; } = [];
        public List<string> DeletedMessageIds { get; } = [];

        public Task IndexAsync(
            Organization organization,
            Guid sourceId,
            string tenantId,
            string mailboxUserId,
            Microsoft365OutlookMessageDelta message,
            CancellationToken cancellationToken = default)
        {
            IndexedMessages.Add((organization.Id, sourceId, tenantId, mailboxUserId, message));
            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            Guid organizationId,
            Guid sourceId,
            string messageId,
            CancellationToken cancellationToken = default)
        {
            DeletedMessageIds.Add(messageId);
            return Task.CompletedTask;
        }
    }

    private sealed class StubDeltaClient(IReadOnlyCollection<Microsoft365OutlookMessageDeltaPage> pages)
        : IMicrosoft365OutlookMessageDeltaClient
    {
        public int InitialCallCount { get; private set; }
        public DateTimeOffset? ReceivedSince { get; private set; }

        public async IAsyncEnumerable<Microsoft365OutlookMessageDeltaPage> GetInitialPagesAsync(
            string tenantId,
            string mailboxUserId,
            string mailFolderId,
            DateTimeOffset receivedSince,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            InitialCallCount++;
            ReceivedSince = receivedSince;
            foreach (var page in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return page;
                await Task.Yield();
            }
        }

        public IAsyncEnumerable<Microsoft365OutlookMessageDeltaPage> GetDeltaPagesAsync(
            string tenantId,
            string deltaLink,
            CancellationToken cancellationToken = default) =>
            GetInitialPagesAsync(tenantId, string.Empty, string.Empty, DateTimeOffset.MinValue, cancellationToken);
    }

    private sealed class InvalidCheckpointThenInitialClient(
        IReadOnlyCollection<Microsoft365OutlookMessageDeltaPage> initialPages)
        : IMicrosoft365OutlookMessageDeltaClient
    {
        public int InitialCallCount { get; private set; }
        public string? ReceivedDeltaLink { get; private set; }

        public async IAsyncEnumerable<Microsoft365OutlookMessageDeltaPage> GetInitialPagesAsync(
            string tenantId,
            string mailboxUserId,
            string mailFolderId,
            DateTimeOffset receivedSince,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            InitialCallCount++;
            foreach (var page in initialPages)
            {
                yield return page;
                await Task.Yield();
            }
        }

        public async IAsyncEnumerable<Microsoft365OutlookMessageDeltaPage> GetDeltaPagesAsync(
            string tenantId,
            string deltaLink,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ReceivedDeltaLink = deltaLink;
            await Task.Yield();
            throw new Microsoft365DeltaCheckpointInvalidException("Expired delta checkpoint.");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
