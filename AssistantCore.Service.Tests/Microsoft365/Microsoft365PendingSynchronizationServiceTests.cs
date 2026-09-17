using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Services.Microsoft365;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365PendingSynchronizationServiceTests
{
    [Theory, AutoDomainData]
    public async Task Given_APendingListSynchronization_When_ProcessNextAsync_Then_StartsTheListSynchronization(
        Guid synchronizationId,
        Guid sourceId,
        Guid organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var work = CreateWork(
            synchronizationId,
            sourceId,
            organizationId,
            Microsoft365SourceKind.SharePointList,
            Microsoft365SynchronizationType.Initial);
        var listService = new StubListSynchronizationService();
        var service = CreateService(work, listService: listService, now: now);

        var processed = await service.ProcessNextAsync(cancellationToken);

        Assert.True(processed);
        Assert.Equal((sourceId, synchronizationId), listService.InitialSynchronization);
        Assert.Equal(cancellationToken, listService.ReceivedCancellationToken);
    }

    [Theory, AutoDomainData]
    public async Task Given_APendingDriveSynchronization_When_ProcessNextAsync_Then_StartsTheDriveSynchronization(
        Guid synchronizationId,
        Guid sourceId,
        Guid organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var work = CreateWork(
            synchronizationId,
            sourceId,
            organizationId,
            Microsoft365SourceKind.SharePointDrive,
            Microsoft365SynchronizationType.Delta);
        var driveService = new StubDriveSynchronizationService();
        var service = CreateService(work, driveService: driveService, now: now);

        var processed = await service.ProcessNextAsync(cancellationToken);

        Assert.True(processed);
        Assert.Equal((sourceId, synchronizationId), driveService.DeltaSynchronization);
        Assert.Equal(cancellationToken, driveService.ReceivedCancellationToken);
    }

    [Theory, AutoDomainData]
    public async Task Given_APendingOneDriveSynchronization_When_ProcessNextAsync_Then_StartsTheDriveSynchronization(
        Guid synchronizationId,
        Guid sourceId,
        Guid organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var work = CreateWork(
            synchronizationId,
            sourceId,
            organizationId,
            Microsoft365SourceKind.OneDrive,
            Microsoft365SynchronizationType.Initial);
        var driveService = new StubDriveSynchronizationService();
        var service = CreateService(work, driveService: driveService, now: now);

        var processed = await service.ProcessNextAsync(cancellationToken);

        Assert.True(processed);
        Assert.Equal((sourceId, synchronizationId), driveService.InitialSynchronization);
        Assert.Equal(cancellationToken, driveService.ReceivedCancellationToken);
    }

    [Theory, AutoDomainData]
    public async Task Given_APendingOutlookInitialSynchronization_When_ProcessNextAsync_Then_StartsOutlookSynchronization(
        Guid synchronizationId,
        Guid sourceId,
        Guid organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var work = CreateWork(
            synchronizationId,
            sourceId,
            organizationId,
            Microsoft365SourceKind.OutlookMailbox,
            Microsoft365SynchronizationType.Initial);
        var outlookService = new StubOutlookSynchronizationService();
        var service = CreateService(work, outlookService: outlookService, now: now);

        var processed = await service.ProcessNextAsync(cancellationToken);

        Assert.True(processed);
        Assert.Equal((sourceId, synchronizationId), outlookService.InitialSynchronization);
        Assert.Null(outlookService.DeltaSynchronization);
        Assert.Equal(cancellationToken, outlookService.ReceivedCancellationToken);
    }

    [Theory, AutoDomainData]
    public async Task Given_APendingOutlookDeltaSynchronization_When_ProcessNextAsync_Then_StartsOutlookDeltaSynchronization(
        Guid synchronizationId,
        Guid sourceId,
        Guid organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var work = CreateWork(
            synchronizationId,
            sourceId,
            organizationId,
            Microsoft365SourceKind.OutlookMailbox,
            Microsoft365SynchronizationType.Delta);
        var outlookService = new StubOutlookSynchronizationService();
        var service = CreateService(work, outlookService: outlookService, now: now);

        var processed = await service.ProcessNextAsync(cancellationToken);

        Assert.True(processed);
        Assert.Equal((sourceId, synchronizationId), outlookService.DeltaSynchronization);
        Assert.Null(outlookService.InitialSynchronization);
        Assert.Equal(cancellationToken, outlookService.ReceivedCancellationToken);
    }

    [Theory, AutoDomainData]
    public async Task Given_APendingIndexCleanup_When_ProcessNextAsync_Then_CleansTheSourceIndex(
        Guid synchronizationId,
        Guid sourceId,
        Guid organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var work = CreateWork(
            synchronizationId,
            sourceId,
            organizationId,
            Microsoft365SourceKind.SharePointList,
            Microsoft365SynchronizationType.IndexCleanup,
            isIndexed: false,
            sourceStatus: Microsoft365SourceStatus.Disabled);
        var cleanupService = new StubIndexCleanupService();
        var service = CreateService(work, cleanupService: cleanupService, now: now);

        var processed = await service.ProcessNextAsync(cancellationToken);

        Assert.True(processed);
        Assert.Equal(
            (organizationId, sourceId, synchronizationId),
            cleanupService.CleanupRequest);
        Assert.Equal(cancellationToken, cleanupService.ReceivedCancellationToken);
    }

    [Theory, AutoDomainData]
    public async Task Given_AStaleSynchronizationForADisabledSource_When_ProcessNextAsync_Then_CancelsTheSynchronization(
        Guid synchronizationId,
        Guid sourceId,
        Guid organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var work = CreateWork(
            synchronizationId,
            sourceId,
            organizationId,
            Microsoft365SourceKind.SharePointDrive,
            Microsoft365SynchronizationType.Initial,
            isIndexed: false,
            sourceStatus: Microsoft365SourceStatus.Disabled);
        var outcomeRepository = new StubSourceSynchronizationRepository();
        var service = CreateService(
            work,
            outcomeRepository: outcomeRepository,
            now: now);

        var processed = await service.ProcessNextAsync(cancellationToken);

        Assert.True(processed);
        Assert.Equal(Microsoft365SynchronizationStatus.Cancelled, outcomeRepository.Status);
        Assert.Equal(sourceId, outcomeRepository.SourceId);
        Assert.Equal(synchronizationId, outcomeRepository.SynchronizationId);
    }

    private static Microsoft365PendingSynchronizationService CreateService(
        Microsoft365PendingSynchronization work,
        StubDriveSynchronizationService? driveService = null,
        StubListSynchronizationService? listService = null,
        StubOutlookSynchronizationService? outlookService = null,
        StubIndexCleanupService? cleanupService = null,
        StubSourceSynchronizationRepository? outcomeRepository = null,
        DateTimeOffset? now = null) =>
        new(
            new StubPendingSynchronizationRepository(work),
            driveService ?? new StubDriveSynchronizationService(),
            listService ?? new StubListSynchronizationService(),
            outlookService ?? new StubOutlookSynchronizationService(),
            cleanupService ?? new StubIndexCleanupService(),
            outcomeRepository ?? new StubSourceSynchronizationRepository(),
            new FixedTimeProvider(now ?? DateTimeOffset.UtcNow));

    [Theory, AutoDomainData]
    public async Task Given_ASynchronizationOfAReindex_When_ProcessNextAsync_Then_ForwardsTheReindexOperation(
        Guid synchronizationId,
        Guid sourceId,
        Guid organizationId,
        Guid reindexOperationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Given
        var work = CreateWork(
            synchronizationId,
            sourceId,
            organizationId,
            Microsoft365SourceKind.SharePointDrive,
            Microsoft365SynchronizationType.Initial,
            reindexOperationId: reindexOperationId);
        var driveService = new StubDriveSynchronizationService();
        var service = CreateService(work, driveService: driveService, now: now);

        // When
        await service.ProcessNextAsync(cancellationToken);

        // Then
        Assert.Equal((sourceId, synchronizationId), driveService.InitialSynchronization);
        Assert.Equal(reindexOperationId, driveService.ReceivedReindexOperationId);
    }

    [Theory, AutoDomainData]
    public async Task Given_ASynchronizationOutsideAReindex_When_ProcessNextAsync_Then_ForwardsNoReindexOperation(
        Guid synchronizationId,
        Guid sourceId,
        Guid organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Given
        var work = CreateWork(
            synchronizationId,
            sourceId,
            organizationId,
            Microsoft365SourceKind.SharePointDrive,
            Microsoft365SynchronizationType.Initial);
        var driveService = new StubDriveSynchronizationService();
        var service = CreateService(work, driveService: driveService, now: now);

        // When
        await service.ProcessNextAsync(cancellationToken);

        // Then
        Assert.Null(driveService.ReceivedReindexOperationId);
    }

    private static Microsoft365PendingSynchronization CreateWork(
        Guid synchronizationId,
        Guid sourceId,
        Guid organizationId,
        Microsoft365SourceKind sourceKind,
        Microsoft365SynchronizationType type,
        bool isIndexed = true,
        Microsoft365SourceStatus sourceStatus = Microsoft365SourceStatus.Enabled,
        Guid? reindexOperationId = null) =>
        new(
            synchronizationId,
            sourceId,
            organizationId,
            sourceKind,
            sourceStatus,
            isIndexed,
            type,
            reindexOperationId);

    private sealed class StubPendingSynchronizationRepository(
        Microsoft365PendingSynchronization work) : IMicrosoft365PendingSynchronizationRepository
    {
        public Task<Microsoft365PendingSynchronization?> ClaimNextAsync(
            DateTimeOffset startedAt,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Microsoft365PendingSynchronization?>(work);
    }

    private sealed class StubDriveSynchronizationService : IMicrosoft365DriveSynchronizationService
    {
        public (Guid SourceId, Guid SynchronizationId)? InitialSynchronization { get; private set; }
        public (Guid SourceId, Guid SynchronizationId)? DeltaSynchronization { get; private set; }
        public Guid? ReceivedReindexOperationId { get; private set; }
        public CancellationToken ReceivedCancellationToken { get; private set; }

        public Task<Microsoft365DriveInitialSynchronizationResult> StartInitialSynchronizationAsync(
            Guid sourceId,
            Guid synchronizationId,
            Guid? reindexOperationId = null,
            CancellationToken cancellationToken = default)
        {
            InitialSynchronization = (sourceId, synchronizationId);
            ReceivedReindexOperationId = reindexOperationId;
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(new Microsoft365DriveInitialSynchronizationResult(
                Microsoft365DriveInitialSynchronizationStatus.Completed,
                0, 0, 0, 0, 0, null));
        }

        public Task<Microsoft365DriveDeltaSynchronizationResult> StartDeltaSynchronizationAsync(
            Guid sourceId,
            Guid synchronizationId,
            CancellationToken cancellationToken = default)
        {
            DeltaSynchronization = (sourceId, synchronizationId);
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(new Microsoft365DriveDeltaSynchronizationResult(
                Microsoft365DriveDeltaSynchronizationStatus.Completed,
                0, 0, 0, 0, 0, null));
        }
    }

    private sealed class StubListSynchronizationService : IMicrosoft365ListSynchronizationService
    {
        public (Guid SourceId, Guid SynchronizationId)? InitialSynchronization { get; private set; }
        public CancellationToken ReceivedCancellationToken { get; private set; }

        public Task<Microsoft365ListInitialSynchronizationResult> StartInitialSynchronizationAsync(
            Guid sourceId,
            Guid synchronizationId,
            CancellationToken cancellationToken = default)
        {
            InitialSynchronization = (sourceId, synchronizationId);
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(new Microsoft365ListInitialSynchronizationResult(
                Microsoft365ListInitialSynchronizationStatus.Completed,
                0, 0, 0, null));
        }

        public Task<Microsoft365ListDeltaSynchronizationResult> StartDeltaSynchronizationAsync(
            Guid sourceId,
            Guid synchronizationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new Microsoft365ListDeltaSynchronizationResult(
                Microsoft365ListDeltaSynchronizationStatus.Completed,
                0, 0, 0, null));

        public Task<Microsoft365ListSchemaSynchronizationResult> SynchronizeSchemaAsync(
            Guid sourceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new Microsoft365ListSchemaSynchronizationResult(
                Microsoft365ListSchemaSynchronizationStatus.Unchanged,
                null,
                false));
    }

    private sealed class StubOutlookSynchronizationService : IMicrosoft365OutlookSynchronizationService
    {
        public (Guid SourceId, Guid SynchronizationId)? InitialSynchronization { get; private set; }
        public (Guid SourceId, Guid SynchronizationId)? DeltaSynchronization { get; private set; }
        public CancellationToken ReceivedCancellationToken { get; private set; }

        public Task StartInitialSynchronizationAsync(
            Guid sourceId,
            Guid synchronizationId,
            CancellationToken cancellationToken = default)
        {
            InitialSynchronization = (sourceId, synchronizationId);
            ReceivedCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        public Task StartDeltaSynchronizationAsync(
            Guid sourceId,
            Guid synchronizationId,
            CancellationToken cancellationToken = default)
        {
            DeltaSynchronization = (sourceId, synchronizationId);
            ReceivedCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class StubIndexCleanupService : IMicrosoft365IndexCleanupService
    {
        public (Guid OrganizationId, Guid SourceId, Guid SynchronizationId)? CleanupRequest { get; private set; }
        public CancellationToken ReceivedCancellationToken { get; private set; }

        public Task CleanupAsync(
            Guid organizationId,
            Guid sourceId,
            Guid synchronizationId,
            CancellationToken cancellationToken = default)
        {
            CleanupRequest = (organizationId, sourceId, synchronizationId);
            ReceivedCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class StubSourceSynchronizationRepository : IMicrosoft365SourceSynchronizationRepository
    {
        public Guid? SourceId { get; private set; }
        public Guid? SynchronizationId { get; private set; }
        public Microsoft365SynchronizationStatus? Status { get; private set; }

        public Task<bool> RecordSynchronizationOutcomeAsync(
            Guid sourceId,
            Guid synchronizationId,
            Microsoft365SynchronizationStatus status,
            Microsoft365SynchronizationCounters counters,
            DateTimeOffset completedAt,
            string? lastErrorCode,
            CancellationToken cancellationToken = default)
        {
            SourceId = sourceId;
            SynchronizationId = synchronizationId;
            Status = status;
            return Task.FromResult(true);
        }

        public Task<bool> TryAcquireLeaseAsync(Guid sourceId, Guid leaseId, DateTimeOffset attemptedAt, DateTimeOffset expiresAt, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> ConfirmCheckpointAsync(Guid sourceId, Guid leaseId, string deltaLink, DateTimeOffset completedAt, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> MarkFullResyncRequiredAsync(Guid sourceId, Guid leaseId, string lastErrorCode, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> MarkAccessErrorAsync(Guid sourceId, Guid leaseId, string lastErrorCode, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ReleaseLeaseAsync(Guid sourceId, Guid leaseId, string? lastErrorCode, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
