using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365DriveSynchronizationServiceTests
{
    [Theory, AutoDomainData]
    public async Task Given_FilesFoldersAndDeletion_When_StartInitialSynchronizationAsync_Then_PersistsOnlyDocumentWork(
        Guid sourceId,
        Guid synchronizationId,
        DateTimeOffset now)
    {
        // Given
        const string deltaLink = "https://graph.microsoft.com/v1.0/drives/drive/root/delta?token=opaque";
        var repository = new RecordingRepository(CreateDrive(sourceId));
        var pages = new[]
        {
            new Microsoft365DriveItemDeltaPage(
                new[]
                {
                    CreateItem("file", "report.pdf", "application/pdf", isDeleted: false, isFolder: false, isFile: true),
                    CreateItem("folder", "records", null, isDeleted: false, isFolder: true, isFile: false),
                    CreateItem("video", "meeting.mp4", "video/mp4", isDeleted: false, isFolder: false, isFile: true),
                    CreateItem("deleted-folder", null, null, isDeleted: true, isFolder: true, isFile: false)
                },
                deltaLink)
        };
        var service = CreateService(repository, new StubDeltaClient(pages), now);

        // When
        var result = await service.StartInitialSynchronizationAsync(
            sourceId,
            synchronizationId,
            cancellationToken: CancellationToken.None);

        // Then
        Assert.Equal(Microsoft365DriveInitialSynchronizationStatus.Completed, result.Status);
        Assert.Equal(1, result.ProcessWorkCount);
        Assert.Equal(1, result.DeleteWorkCount);
        Assert.Equal(1, result.IgnoredFolderCount);
        Assert.Equal(1, result.IgnoredUnsupportedFileCount);
        Assert.Equal(2, result.PersistedWorkCount);
        Assert.Equal(deltaLink, result.DeltaLink);
        Assert.Equal(deltaLink, repository.ConfirmedDeltaLink);
        Assert.Equal(now, repository.CheckpointConfirmedAt);
        Assert.Equal(1, repository.SavedPageCountWhenCheckpointConfirmed);
        Assert.Equal(Microsoft365SynchronizationStatus.Succeeded, repository.RecordedStatus);
        Assert.Equal(1, repository.RecordedCounters?.ModifiedCount);
        Assert.Equal(1, repository.RecordedCounters?.DeletedCount);
        Assert.Equal(2, repository.RecordedCounters?.IgnoredCount);
        Assert.Equal(0, repository.RecordedCounters?.FailedCount);
        var savedWorks = Assert.Single(repository.SavedWorkPages);
        Assert.Contains(savedWorks, work => work.WorkType == Microsoft365DocumentWorkType.ProcessDocument);
        Assert.Contains(savedWorks, work =>
            work.WorkType == Microsoft365DocumentWorkType.DeleteDocument
            && work.DriveItemId == "deleted-folder");
        Assert.Equal(1, repository.ReleaseCount);
        Assert.Null(repository.ReleasedErrorCode);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnAlreadyLeasedDrive_When_StartInitialSynchronizationAsync_Then_DoesNotCallDelta(
        Guid sourceId,
        Guid synchronizationId,
        DateTimeOffset now)
    {
        // Given
        var repository = new RecordingRepository(CreateDrive(sourceId)) { LeaseIsAvailable = false };
        var deltaClient = new StubDeltaClient([]);
        var service = CreateService(repository, deltaClient, now);

        // When
        var result = await service.StartInitialSynchronizationAsync(
            sourceId,
            synchronizationId,
            cancellationToken: CancellationToken.None);

        // Then
        Assert.Equal(Microsoft365DriveInitialSynchronizationStatus.AlreadyInProgress, result.Status);
        Assert.Equal(0, deltaClient.CallCount);
        Assert.Equal(0, repository.ReleaseCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_AStoredDriveCheckpoint_When_StartDeltaSynchronizationAsync_Then_UsesStoredLinkAndConfirmsNextLink(
        Guid sourceId,
        Guid synchronizationId,
        DateTimeOffset now)
    {
        // Given
        const string storedDeltaLink = "https://graph.microsoft.com/v1.0/drives/drive/root/delta?token=stored%2Bopaque%3D";
        const string nextDeltaLink = "https://graph.microsoft.com/v1.0/drives/drive/root/delta?token=next%2Bopaque%3D";
        var drive = CreateDrive(sourceId);
        drive.DeltaLink = storedDeltaLink;
        var repository = new RecordingRepository(drive);
        var item = CreateItem(
            "file",
            "report.pdf",
            "application/pdf",
            isDeleted: false,
            isFolder: false,
            isFile: true);
        var deltaClient = new StubDeltaClient(
            [new Microsoft365DriveItemDeltaPage([item, item], nextDeltaLink)]);
        var service = CreateService(repository, deltaClient, now);

        // When
        var result = await service.StartDeltaSynchronizationAsync(
            sourceId,
            synchronizationId,
            CancellationToken.None);

        // Then
        Assert.Equal(Microsoft365DriveDeltaSynchronizationStatus.Completed, result.Status);
        Assert.Equal(storedDeltaLink, deltaClient.DeltaLink);
        Assert.Equal(2, result.ProcessWorkCount);
        Assert.Equal(1, result.PersistedWorkCount);
        Assert.Equal(nextDeltaLink, repository.ConfirmedDeltaLink);
    }

    [Theory, AutoDomainData]
    public async Task Given_MicrosoftDeniesDriveAccess_When_StartDeltaSynchronizationAsync_Then_MarksSourceAccessError(
        Guid sourceId,
        Guid synchronizationId,
        DateTimeOffset now)
    {
        // Given
        var drive = CreateDrive(sourceId);
        drive.DeltaLink = "https://graph.microsoft.com/v1.0/drives/drive/root/delta?token=stored";
        var repository = new RecordingRepository(drive);
        var service = CreateService(
            repository,
            new ThrowingDriveDeltaClient(
                new Microsoft365SourceAccessDeniedException("Access denied.")),
            now);

        // When
        var action = () => service.StartDeltaSynchronizationAsync(
            sourceId,
            synchronizationId,
            CancellationToken.None);

        // Then
        await Assert.ThrowsAsync<Microsoft365SourceAccessDeniedException>(action);
        Assert.Equal(Microsoft365SourceStatus.Error, drive.Status);
        Assert.Equal(Microsoft365SynchronizationStatus.PermanentFailure, repository.RecordedStatus);
        Assert.Equal("MicrosoftGraphDriveAccessDenied", repository.RecordedErrorCode);
        Assert.Equal(1, repository.RecordedCounters?.FailedCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnInvalidDriveDeltaCheckpoint_When_StartDeltaSynchronizationAsync_Then_MarksSourceAndRunsFullResync(
        Guid sourceId,
        Guid synchronizationId,
        DateTimeOffset now)
    {
        // Given
        const string storedDeltaLink = "https://graph.microsoft.com/v1.0/drives/drive/root/delta?token=expired";
        const string nextDeltaLink = "https://graph.microsoft.com/v1.0/drives/drive/root/delta?token=fresh";
        var drive = CreateDrive(sourceId);
        drive.DeltaLink = storedDeltaLink;
        var repository = new RecordingRepository(drive);
        var fullResyncItem = CreateItem(
            "file",
            "report.pdf",
            "application/pdf",
            isDeleted: false,
            isFolder: false,
            isFile: true);
        var deltaClient = new InvalidCheckpointThenInitialDeltaClient(
            [new Microsoft365DriveItemDeltaPage([fullResyncItem], nextDeltaLink)]);
        var service = CreateService(repository, deltaClient, now);

        // When
        var result = await service.StartDeltaSynchronizationAsync(
            sourceId,
            synchronizationId,
            CancellationToken.None);

        // Then
        Assert.Equal(Microsoft365DriveDeltaSynchronizationStatus.Completed, result.Status);
        Assert.Equal(storedDeltaLink, deltaClient.DeltaLink);
        Assert.Equal(1, deltaClient.InitialCallCount);
        Assert.Equal(Microsoft365SourceStatus.FullResyncRequired, repository.MarkedStatus);
        Assert.Equal("MicrosoftGraphDriveDeltaCheckpointInvalid", repository.FullResyncRequiredErrorCode);
        Assert.Equal(1, result.ProcessWorkCount);
        Assert.Equal(nextDeltaLink, repository.ConfirmedDeltaLink);
        Assert.Equal(Microsoft365SourceStatus.Enabled, drive.Status);
        Assert.Equal(1, repository.SavedPageCountWhenCheckpointConfirmed);
        Assert.Null(repository.ReleasedErrorCode);
    }

    private static Microsoft365DriveSynchronizationService CreateService(
        RecordingRepository repository,
        IMicrosoft365DriveItemDeltaClient deltaClient,
        DateTimeOffset now) =>
        new(
            repository,
            repository,
            deltaClient,
            new Microsoft365DocumentSupportPolicy(),
            new Microsoft365DocumentWorkFactory(),
            Options.Create(new Microsoft365Options { SynchronizationLeaseMinutes = 15 }),
            new FixedTimeProvider(now));

    private static Microsoft365Drive CreateDrive(Guid sourceId) =>
        new()
        {
            Id = sourceId,
            OrganizationId = Guid.NewGuid(),
            SiteId = "site-id",
            DriveId = "drive-id",
            IsIndexed = true,
            Status = Microsoft365SourceStatus.Enabled,
            Microsoft365Connection = new Microsoft365Connection
            {
                TenantId = "tenant-id",
                Status = Microsoft365ConnectionStatus.Active,
                OrganizationConnector = new OrganizationConnector
                {
                    Status = RecordStatus.Active,
                    IsConfigured = true
                }
            }
        };

    private static Microsoft365DriveItemDelta CreateItem(
        string id,
        string? name,
        string? mimeType,
        bool isDeleted,
        bool isFolder,
        bool isFile) =>
        new(
            id,
            name,
            isDeleted ? null : "etag",
            null,
            null,
            null,
            isDeleted ? null : 42,
            mimeType,
            isDeleted,
            isFolder,
            isFile);

    private sealed class RecordingRepository(Microsoft365Drive drive)
        : IMicrosoft365DriveSynchronizationRepository, IMicrosoft365SourceSynchronizationRepository
    {
        private readonly HashSet<string> persistedKeys = new(StringComparer.Ordinal);

        public bool LeaseIsAvailable { get; init; } = true;

        public int ReleaseCount { get; private set; }

        public string? ReleasedErrorCode { get; private set; }

        public string? ConfirmedDeltaLink { get; private set; }

        public DateTimeOffset? CheckpointConfirmedAt { get; private set; }

        public int? SavedPageCountWhenCheckpointConfirmed { get; private set; }

        public Microsoft365SourceStatus? MarkedStatus { get; private set; }

        public string? FullResyncRequiredErrorCode { get; private set; }

        public Microsoft365SynchronizationStatus? RecordedStatus { get; private set; }

        public Microsoft365SynchronizationCounters? RecordedCounters { get; private set; }

        public string? RecordedErrorCode { get; private set; }

        public List<IReadOnlyCollection<Microsoft365DocumentWorkData>> SavedWorkPages { get; } = [];

        public Task<Microsoft365Drive?> FindForSynchronizationAsync(
            Guid sourceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Microsoft365Drive?>(drive.Id == sourceId ? drive : null);

        public Task<bool> TryAcquireLeaseAsync(
            Guid sourceId,
            Guid leaseId,
            DateTimeOffset attemptedAt,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(LeaseIsAvailable);

        public Task<bool> ConfirmCheckpointAsync(
            Guid sourceId,
            Guid leaseId,
            string deltaLink,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default)
        {
            ConfirmedDeltaLink = deltaLink;
            CheckpointConfirmedAt = completedAt;
            SavedPageCountWhenCheckpointConfirmed = SavedWorkPages.Count;
            drive.DeltaLink = deltaLink;
            if (drive.Status == Microsoft365SourceStatus.FullResyncRequired)
            {
                drive.Status = Microsoft365SourceStatus.Enabled;
            }

            return Task.FromResult(true);
        }

        public Task<bool> MarkFullResyncRequiredAsync(
            Guid sourceId,
            Guid leaseId,
            string lastErrorCode,
            CancellationToken cancellationToken = default)
        {
            drive.Status = Microsoft365SourceStatus.FullResyncRequired;
            MarkedStatus = drive.Status;
            FullResyncRequiredErrorCode = lastErrorCode;
            return Task.FromResult(true);
        }

        public Task<bool> MarkAccessErrorAsync(
            Guid sourceId,
            Guid leaseId,
            string lastErrorCode,
            CancellationToken cancellationToken = default)
        {
            drive.Status = Microsoft365SourceStatus.Error;
            ReleasedErrorCode = lastErrorCode;
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
            RecordedErrorCode = lastErrorCode;
            return Task.FromResult(true);
        }

        public Task<int> SaveWorkPageAsync(
            Guid sourceId,
            Guid synchronizationId,
            Guid leaseId,
            DateTimeOffset leaseExpiresAt,
            IReadOnlyCollection<Microsoft365DocumentWorkData> works,
            CancellationToken cancellationToken = default)
        {
            SavedWorkPages.Add(works.ToArray());
            return Task.FromResult(works.Count(work => persistedKeys.Add(work.DeduplicationKey)));
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

    private sealed class StubDeltaClient(IReadOnlyCollection<Microsoft365DriveItemDeltaPage> pages)
        : IMicrosoft365DriveItemDeltaClient
    {
        public int CallCount { get; private set; }

        public string? DeltaLink { get; private set; }

        public async IAsyncEnumerable<Microsoft365DriveItemDeltaPage> GetInitialPagesAsync(
            string tenantId,
            string driveId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CallCount++;
            foreach (var page in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return page;
                await Task.Yield();
            }
        }

        public IAsyncEnumerable<Microsoft365DriveItemDeltaPage> GetDeltaPagesAsync(
            string tenantId,
            string deltaLink,
            CancellationToken cancellationToken = default)
        {
            DeltaLink = deltaLink;
            return GetInitialPagesAsync(tenantId, string.Empty, cancellationToken);
        }
    }

    private sealed class InvalidCheckpointThenInitialDeltaClient(
        IReadOnlyCollection<Microsoft365DriveItemDeltaPage> initialPages)
        : IMicrosoft365DriveItemDeltaClient
    {
        public int InitialCallCount { get; private set; }

        public string? DeltaLink { get; private set; }

        public async IAsyncEnumerable<Microsoft365DriveItemDeltaPage> GetInitialPagesAsync(
            string tenantId,
            string driveId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            InitialCallCount++;
            foreach (var page in initialPages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return page;
                await Task.Yield();
            }
        }

        public async IAsyncEnumerable<Microsoft365DriveItemDeltaPage> GetDeltaPagesAsync(
            string tenantId,
            string deltaLink,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            DeltaLink = deltaLink;
            await Task.Yield();
            throw new Microsoft365DeltaCheckpointInvalidException("Delta checkpoint is invalid.");
            #pragma warning disable CS0162
            yield break;
            #pragma warning restore CS0162
        }
    }

    private sealed class ThrowingDriveDeltaClient(Exception exception)
        : IMicrosoft365DriveItemDeltaClient
    {
        public async IAsyncEnumerable<Microsoft365DriveItemDeltaPage> GetInitialPagesAsync(
            string tenantId,
            string driveId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw exception;
            #pragma warning disable CS0162
            yield break;
            #pragma warning restore CS0162
        }

        public async IAsyncEnumerable<Microsoft365DriveItemDeltaPage> GetDeltaPagesAsync(
            string tenantId,
            string deltaLink,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw exception;
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
