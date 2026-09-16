using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Microsoft365;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public sealed class Microsoft365DriveSynchronizationService(
    IMicrosoft365DriveSynchronizationRepository synchronizationRepository,
    IMicrosoft365SourceSynchronizationRepository sourceSynchronizationRepository,
    IMicrosoft365DriveItemDeltaClient deltaClient,
    IMicrosoft365DocumentSupportPolicy supportPolicy,
    IMicrosoft365DocumentWorkFactory workFactory,
    IOptions<Microsoft365Options> options,
    TimeProvider timeProvider) : IMicrosoft365DriveSynchronizationService
{
    private const string InitialSynchronizationFailedErrorCode = "MicrosoftGraphInitialDriveSynchronizationFailed";
    private const string InitialSynchronizationCancelledErrorCode = "MicrosoftGraphInitialDriveSynchronizationCancelled";
    private const string DeltaSynchronizationFailedErrorCode = "MicrosoftGraphDeltaDriveSynchronizationFailed";
    private const string DeltaSynchronizationCancelledErrorCode = "MicrosoftGraphDeltaDriveSynchronizationCancelled";
    private const string DeltaCheckpointInvalidErrorCode = "MicrosoftGraphDriveDeltaCheckpointInvalid";

    public async Task<Microsoft365DriveInitialSynchronizationResult> StartInitialSynchronizationAsync(
        Guid sourceId,
        Guid synchronizationId,
        Guid? reindexOperationId = null,
        CancellationToken cancellationToken = default)
    {
        var lease = await PrepareLeaseAsync(sourceId, cancellationToken);
        if (!lease.IsAcquired)
        {
            return new Microsoft365DriveInitialSynchronizationResult(
                Microsoft365DriveInitialSynchronizationStatus.AlreadyInProgress,
                0,
                0,
                0,
                0,
                0,
                null);
        }

        return await ExecuteUnderLeaseAsync(
            lease,
            synchronizationId,
            InitialSynchronizationFailedErrorCode,
            InitialSynchronizationCancelledErrorCode,
            async () =>
            {
                var synchronization = await SynchronizeItemsAsync(
                    lease,
                    synchronizationId,
                    reindexOperationId,
                    deltaClient.GetInitialPagesAsync(
                        lease.Drive.Microsoft365Connection.TenantId!,
                        lease.Drive.DriveId,
                        cancellationToken),
                    cancellationToken);
                var result = new Microsoft365DriveInitialSynchronizationResult(
                    Microsoft365DriveInitialSynchronizationStatus.Completed,
                    synchronization.ProcessWorkCount,
                    synchronization.DeleteWorkCount,
                    synchronization.IgnoredFolderCount,
                    synchronization.IgnoredUnsupportedFileCount,
                    synchronization.PersistedWorkCount,
                    synchronization.DeltaLink);
                return (result, synchronization.Counters);
            });
    }

    public async Task<Microsoft365DriveDeltaSynchronizationResult> StartDeltaSynchronizationAsync(
        Guid sourceId,
        Guid synchronizationId,
        CancellationToken cancellationToken = default)
    {
        var lease = await PrepareLeaseAsync(sourceId, cancellationToken);
        if (!lease.IsAcquired)
        {
            return new Microsoft365DriveDeltaSynchronizationResult(
                Microsoft365DriveDeltaSynchronizationStatus.AlreadyInProgress,
                0,
                0,
                0,
                0,
                0,
                null);
        }

        return await ExecuteUnderLeaseAsync(
            lease,
            synchronizationId,
            DeltaSynchronizationFailedErrorCode,
            DeltaSynchronizationCancelledErrorCode,
            async () =>
            {
                ItemSynchronizationResult synchronization;
                if (lease.Drive.Status == Microsoft365SourceStatus.FullResyncRequired)
                {
                    synchronization = await SynchronizeItemsAsync(
                        lease,
                        synchronizationId,
                        reindexOperationId: null,
                        deltaClient.GetInitialPagesAsync(
                            lease.Drive.Microsoft365Connection.TenantId!,
                            lease.Drive.DriveId,
                            cancellationToken),
                        cancellationToken);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(lease.Drive.DeltaLink))
                    {
                        throw new InvalidOperationException(
                            "The Microsoft 365 drive does not have a confirmed delta checkpoint.");
                    }

                    try
                    {
                        synchronization = await SynchronizeItemsAsync(
                            lease,
                            synchronizationId,
                            reindexOperationId: null,
                            deltaClient.GetDeltaPagesAsync(
                                lease.Drive.Microsoft365Connection.TenantId!,
                                lease.Drive.DeltaLink,
                                cancellationToken),
                            cancellationToken);
                    }
                    catch (Microsoft365DeltaCheckpointInvalidException)
                    {
                        await MarkFullResyncRequiredAsync(
                            lease.Drive.Id,
                            lease.LeaseId,
                            DeltaCheckpointInvalidErrorCode,
                            cancellationToken);
                        synchronization = await SynchronizeItemsAsync(
                            lease,
                            synchronizationId,
                            reindexOperationId: null,
                            deltaClient.GetInitialPagesAsync(
                                lease.Drive.Microsoft365Connection.TenantId!,
                                lease.Drive.DriveId,
                                cancellationToken),
                            cancellationToken);
                    }
                }

                var result = new Microsoft365DriveDeltaSynchronizationResult(
                    Microsoft365DriveDeltaSynchronizationStatus.Completed,
                    synchronization.ProcessWorkCount,
                    synchronization.DeleteWorkCount,
                    synchronization.IgnoredFolderCount,
                    synchronization.IgnoredUnsupportedFileCount,
                    synchronization.PersistedWorkCount,
                    synchronization.DeltaLink);
                return (result, synchronization.Counters);
            });
    }

    private async Task<ItemSynchronizationResult> SynchronizeItemsAsync(
        SynchronizationLease lease,
        Guid synchronizationId,
        Guid? reindexOperationId,
        IAsyncEnumerable<Microsoft365DriveItemDeltaPage> pages,
        CancellationToken cancellationToken)
    {
        var processWorkCount = 0;
        var deleteWorkCount = 0;
        var ignoredFolderCount = 0;
        var ignoredUnsupportedFileCount = 0;
        var persistedWorkCount = 0;
        var createdCount = 0;
        var modifiedCount = 0;
        string? finalDeltaLink = null;

        await foreach (var page in pages)
        {
            var pageCreatedAt = timeProvider.GetUtcNow();
            var works = new List<Microsoft365DocumentWorkData>();
            foreach (var item in page.Items)
            {
                if (item.IsDeleted)
                {
                    works.Add(workFactory.Create(lease.Drive, item, pageCreatedAt, reindexOperationId));
                    deleteWorkCount++;
                }
                else if (item.IsFolder)
                {
                    ignoredFolderCount++;
                }
                else if (item.IsFile
                         && item.Name is not null
                         && supportPolicy.IsSupported(item.Name, item.MimeType))
                {
                    works.Add(workFactory.Create(lease.Drive, item, pageCreatedAt, reindexOperationId));
                    processWorkCount++;
                    if (IsCreated(item))
                    {
                        createdCount++;
                    }
                    else
                    {
                        modifiedCount++;
                    }
                }
                else
                {
                    ignoredUnsupportedFileCount++;
                }
            }

            persistedWorkCount += await synchronizationRepository.SaveWorkPageAsync(
                lease.Drive.Id,
                synchronizationId,
                lease.LeaseId,
                pageCreatedAt.AddMinutes(options.Value.SynchronizationLeaseMinutes),
                works,
                cancellationToken);
            finalDeltaLink = page.DeltaLink ?? finalDeltaLink;
        }

        if (string.IsNullOrWhiteSpace(finalDeltaLink))
        {
            throw new InvalidOperationException(
                "Microsoft Graph did not return a final delta link.");
        }

        var checkpointConfirmed = await sourceSynchronizationRepository.ConfirmCheckpointAsync(
            lease.Drive.Id,
            lease.LeaseId,
            finalDeltaLink,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (!checkpointConfirmed)
        {
            throw new InvalidOperationException(
                "The Microsoft 365 source synchronization lease was lost.");
        }

        return new ItemSynchronizationResult(
            processWorkCount,
            deleteWorkCount,
            ignoredFolderCount,
            ignoredUnsupportedFileCount,
            persistedWorkCount,
            finalDeltaLink,
            new Microsoft365SynchronizationCounters(
                createdCount,
                modifiedCount,
                deleteWorkCount,
                ignoredFolderCount + ignoredUnsupportedFileCount,
                FailedCount: 0));
    }

    private async Task MarkFullResyncRequiredAsync(
        Guid sourceId,
        Guid leaseId,
        string errorCode,
        CancellationToken cancellationToken)
    {
        var statusMarked = await sourceSynchronizationRepository.MarkFullResyncRequiredAsync(
            sourceId,
            leaseId,
            errorCode,
            cancellationToken);
        if (!statusMarked)
        {
            throw new InvalidOperationException(
                "The Microsoft 365 source synchronization lease was lost.");
        }
    }

    private async Task<SynchronizationLease> PrepareLeaseAsync(
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        var drive = await synchronizationRepository.FindForSynchronizationAsync(
            sourceId,
            cancellationToken)
            ?? throw new NotFoundException("Microsoft 365 drive was not found.");
        EnsureDriveCanBeSynchronized(drive);

        var attemptedAt = timeProvider.GetUtcNow();
        var leaseId = Guid.NewGuid();
        var isAcquired = await sourceSynchronizationRepository.TryAcquireLeaseAsync(
            sourceId,
            leaseId,
            attemptedAt,
            attemptedAt.AddMinutes(options.Value.SynchronizationLeaseMinutes),
            cancellationToken);
        return new SynchronizationLease(drive, leaseId, isAcquired);
    }

    private async Task<TResult> ExecuteUnderLeaseAsync<TResult>(
        SynchronizationLease lease,
        Guid synchronizationId,
        string failedErrorCode,
        string cancelledErrorCode,
        Func<Task<(TResult Result, Microsoft365SynchronizationCounters Counters)>> operation)
    {
        string? releaseErrorCode = null;
        try
        {
            var outcome = await operation();
            await RecordSynchronizationOutcomeAsync(
                lease.Drive.Id,
                synchronizationId,
                Microsoft365SynchronizationStatus.Succeeded,
                outcome.Counters,
                timeProvider.GetUtcNow(),
                lastErrorCode: null,
                CancellationToken.None);
            return outcome.Result;
        }
        catch (OperationCanceledException)
        {
            releaseErrorCode = cancelledErrorCode;
            await RecordSynchronizationOutcomeAsync(
                lease.Drive.Id,
                synchronizationId,
                Microsoft365SynchronizationStatus.Cancelled,
                Microsoft365SynchronizationCounters.Empty with { FailedCount = 1 },
                timeProvider.GetUtcNow(),
                cancelledErrorCode,
                CancellationToken.None);
            throw;
        }
        catch (Microsoft365SourceAccessDeniedException)
        {
            releaseErrorCode = "MicrosoftGraphDriveAccessDenied";
            await MarkAccessErrorAsync(
                lease.Drive.Id,
                lease.LeaseId,
                releaseErrorCode,
                CancellationToken.None);
            await RecordSynchronizationOutcomeAsync(
                lease.Drive.Id,
                synchronizationId,
                Microsoft365SynchronizationStatus.PermanentFailure,
                Microsoft365SynchronizationCounters.Empty with { FailedCount = 1 },
                timeProvider.GetUtcNow(),
                releaseErrorCode,
                CancellationToken.None);
            throw;
        }
        catch (Microsoft365GraphTransientException)
        {
            releaseErrorCode = failedErrorCode;
            await RecordSynchronizationOutcomeAsync(
                lease.Drive.Id,
                synchronizationId,
                Microsoft365SynchronizationStatus.TemporaryFailure,
                Microsoft365SynchronizationCounters.Empty with { FailedCount = 1 },
                timeProvider.GetUtcNow(),
                failedErrorCode,
                CancellationToken.None);
            throw;
        }
        catch
        {
            releaseErrorCode = failedErrorCode;
            await RecordSynchronizationOutcomeAsync(
                lease.Drive.Id,
                synchronizationId,
                Microsoft365SynchronizationStatus.PermanentFailure,
                Microsoft365SynchronizationCounters.Empty with { FailedCount = 1 },
                timeProvider.GetUtcNow(),
                failedErrorCode,
                CancellationToken.None);
            throw;
        }
        finally
        {
            await sourceSynchronizationRepository.ReleaseLeaseAsync(
                lease.Drive.Id,
                lease.LeaseId,
                releaseErrorCode,
                CancellationToken.None);
        }
    }

    private async Task MarkAccessErrorAsync(
        Guid sourceId,
        Guid leaseId,
        string errorCode,
        CancellationToken cancellationToken)
    {
        var statusMarked = await sourceSynchronizationRepository.MarkAccessErrorAsync(
            sourceId,
            leaseId,
            errorCode,
            cancellationToken);
        if (!statusMarked)
        {
            throw new InvalidOperationException(
                "The Microsoft 365 source synchronization lease was lost.");
        }
    }

    private async Task RecordSynchronizationOutcomeAsync(
        Guid sourceId,
        Guid synchronizationId,
        Microsoft365SynchronizationStatus status,
        Microsoft365SynchronizationCounters counters,
        DateTimeOffset completedAt,
        string? lastErrorCode,
        CancellationToken cancellationToken)
    {
        var outcomeRecorded = await sourceSynchronizationRepository.RecordSynchronizationOutcomeAsync(
            sourceId,
            synchronizationId,
            status,
            counters,
            completedAt,
            lastErrorCode,
            cancellationToken);
        if (!outcomeRecorded)
        {
            throw new InvalidOperationException(
                "The Microsoft 365 synchronization could not be updated.");
        }
    }

    private static bool IsCreated(Microsoft365DriveItemDelta item) =>
        item.CreatedDateTime is not null
        && item.LastModifiedDateTime is not null
        && item.CreatedDateTime == item.LastModifiedDateTime;

    private static void EnsureDriveCanBeSynchronized(Microsoft365Drive drive)
    {
        var connection = drive.Microsoft365Connection;
        if (!drive.IsIndexed
            || drive.Status is not Microsoft365SourceStatus.Enabled
                and not Microsoft365SourceStatus.FullResyncRequired
            || connection.Status != Microsoft365ConnectionStatus.Active
            || connection.OrganizationConnector.Status != RecordStatus.Active
            || !connection.OrganizationConnector.IsConfigured)
        {
            throw new InvalidOperationException(
                "Only an enabled Microsoft 365 drive attached to an active connector can be synchronized.");
        }

        if (string.IsNullOrWhiteSpace(connection.TenantId))
        {
            throw new InvalidOperationException(
                "The Microsoft 365 connection tenant is missing.");
        }
    }

    private sealed record SynchronizationLease(
        Microsoft365Drive Drive,
        Guid LeaseId,
        bool IsAcquired);

    private sealed record ItemSynchronizationResult(
        int ProcessWorkCount,
        int DeleteWorkCount,
        int IgnoredFolderCount,
        int IgnoredUnsupportedFileCount,
        int PersistedWorkCount,
        string DeltaLink,
        Microsoft365SynchronizationCounters Counters);
}
