using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Microsoft365;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public sealed class Microsoft365OutlookSynchronizationService(
    IMicrosoft365OutlookSynchronizationRepository outlookRepository,
    IMicrosoft365SourceSynchronizationRepository sourceSynchronizationRepository,
    IMicrosoft365OutlookMessageDeltaClient deltaClient,
    IMicrosoft365OutlookMessageIndexingService indexingService,
    IOptions<Microsoft365Options> options,
    TimeProvider timeProvider) : IMicrosoft365OutlookSynchronizationService
{
    private const string InitialSynchronizationFailedErrorCode = "MicrosoftGraphInitialOutlookSynchronizationFailed";
    private const string InitialSynchronizationCancelledErrorCode = "MicrosoftGraphInitialOutlookSynchronizationCancelled";
    private const string DeltaSynchronizationFailedErrorCode = "MicrosoftGraphDeltaOutlookSynchronizationFailed";
    private const string DeltaSynchronizationCancelledErrorCode = "MicrosoftGraphDeltaOutlookSynchronizationCancelled";
    private const string DeltaCheckpointInvalidErrorCode = "MicrosoftGraphOutlookDeltaCheckpointInvalid";
    private const string AccessDeniedErrorCode = "MicrosoftGraphOutlookAccessDenied";

    public Task StartInitialSynchronizationAsync(
        Guid sourceId,
        Guid synchronizationId,
        CancellationToken cancellationToken = default) =>
        ExecuteSynchronizationAsync(
            sourceId,
            synchronizationId,
            InitialSynchronizationFailedErrorCode,
            InitialSynchronizationCancelledErrorCode,
            useDeltaCheckpoint: false,
            cancellationToken);

    public Task StartDeltaSynchronizationAsync(
        Guid sourceId,
        Guid synchronizationId,
        CancellationToken cancellationToken = default) =>
        ExecuteSynchronizationAsync(
            sourceId,
            synchronizationId,
            DeltaSynchronizationFailedErrorCode,
            DeltaSynchronizationCancelledErrorCode,
            useDeltaCheckpoint: true,
            cancellationToken);

    private async Task ExecuteSynchronizationAsync(
        Guid sourceId,
        Guid synchronizationId,
        string failedErrorCode,
        string cancelledErrorCode,
        bool useDeltaCheckpoint,
        CancellationToken cancellationToken)
    {
        var lease = await PrepareLeaseAsync(sourceId, cancellationToken);
        if (!lease.IsAcquired)
        {
            return;
        }

        string? releaseErrorCode = null;
        try
        {
            var counters = useDeltaCheckpoint
                ? await SynchronizeDeltaAsync(lease, cancellationToken)
                : await SynchronizeInitialAsync(lease, cancellationToken);

            await RecordSynchronizationOutcomeAsync(
                lease.Source.Id,
                synchronizationId,
                Microsoft365SynchronizationStatus.Succeeded,
                counters,
                lastErrorCode: null,
                CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            releaseErrorCode = cancelledErrorCode;
            await RecordSynchronizationOutcomeAsync(
                lease.Source.Id,
                synchronizationId,
                Microsoft365SynchronizationStatus.Cancelled,
                Microsoft365SynchronizationCounters.Empty with { FailedCount = 1 },
                cancelledErrorCode,
                CancellationToken.None);
            throw;
        }
        catch (Microsoft365SourceAccessDeniedException)
        {
            releaseErrorCode = AccessDeniedErrorCode;
            await MarkAccessErrorAsync(
                lease.Source.Id,
                lease.LeaseId,
                AccessDeniedErrorCode,
                CancellationToken.None);
            await RecordSynchronizationOutcomeAsync(
                lease.Source.Id,
                synchronizationId,
                Microsoft365SynchronizationStatus.PermanentFailure,
                Microsoft365SynchronizationCounters.Empty with { FailedCount = 1 },
                AccessDeniedErrorCode,
                CancellationToken.None);
            throw;
        }
        catch (Microsoft365GraphTransientException)
        {
            releaseErrorCode = failedErrorCode;
            await RecordSynchronizationOutcomeAsync(
                lease.Source.Id,
                synchronizationId,
                Microsoft365SynchronizationStatus.TemporaryFailure,
                Microsoft365SynchronizationCounters.Empty with { FailedCount = 1 },
                failedErrorCode,
                CancellationToken.None);
            throw;
        }
        catch
        {
            releaseErrorCode = failedErrorCode;
            await RecordSynchronizationOutcomeAsync(
                lease.Source.Id,
                synchronizationId,
                Microsoft365SynchronizationStatus.PermanentFailure,
                Microsoft365SynchronizationCounters.Empty with { FailedCount = 1 },
                failedErrorCode,
                CancellationToken.None);
            throw;
        }
        finally
        {
            await sourceSynchronizationRepository.ReleaseLeaseAsync(
                lease.Source.Id,
                lease.LeaseId,
                releaseErrorCode,
                CancellationToken.None);
        }
    }

    private async Task<Microsoft365SynchronizationCounters> SynchronizeInitialAsync(
        SynchronizationLease lease,
        CancellationToken cancellationToken) =>
        await SynchronizeMessagesAsync(
            lease,
            deltaClient.GetInitialPagesAsync(
                lease.Source.Microsoft365Connection.TenantId!,
                lease.Source.ExternalResourceId,
                lease.Source.ParentExternalResourceId!,
                GetRetentionCutoff(),
                cancellationToken),
            cancellationToken);

    private async Task<Microsoft365SynchronizationCounters> SynchronizeDeltaAsync(
        SynchronizationLease lease,
        CancellationToken cancellationToken)
    {
        if (lease.Source.Status == Microsoft365SourceStatus.FullResyncRequired)
        {
            return await SynchronizeInitialAsync(lease, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(lease.Source.DeltaLink))
        {
            throw new InvalidOperationException(
                "The Outlook source does not have a confirmed delta checkpoint.");
        }

        try
        {
            return await SynchronizeMessagesAsync(
                lease,
                deltaClient.GetDeltaPagesAsync(
                    lease.Source.Microsoft365Connection.TenantId!,
                    lease.Source.DeltaLink,
                    cancellationToken),
                cancellationToken);
        }
        catch (Microsoft365DeltaCheckpointInvalidException)
        {
            var marked = await sourceSynchronizationRepository.MarkFullResyncRequiredAsync(
                lease.Source.Id,
                lease.LeaseId,
                DeltaCheckpointInvalidErrorCode,
                cancellationToken);
            if (!marked)
            {
                throw new InvalidOperationException(
                    "The Microsoft 365 source synchronization lease was lost.");
            }

            return await SynchronizeInitialAsync(lease, cancellationToken);
        }
    }

    private async Task<Microsoft365SynchronizationCounters> SynchronizeMessagesAsync(
        SynchronizationLease lease,
        IAsyncEnumerable<Microsoft365OutlookMessageDeltaPage> pages,
        CancellationToken cancellationToken)
    {
        var createdCount = 0;
        var modifiedCount = 0;
        var deletedCount = 0;
        var ignoredCount = 0;
        string? finalDeltaLink = null;
        var organization = lease.Source.Microsoft365Connection.OrganizationConnector.Organization;
        var retentionCutoff = GetRetentionCutoff();

        await foreach (var page in pages.WithCancellation(cancellationToken))
        {
            foreach (var message in page.Items)
            {
                if (message.IsDeleted)
                {
                    await indexingService.DeleteAsync(
                        organization.Id,
                        lease.Source.Id,
                        message.Id,
                        cancellationToken);
                    deletedCount++;
                    continue;
                }

                if (IsOutsideRetention(message, retentionCutoff))
                {
                    await indexingService.DeleteAsync(
                        organization.Id,
                        lease.Source.Id,
                        message.Id,
                        cancellationToken);
                    ignoredCount++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(message.BodyContent))
                {
                    await indexingService.DeleteAsync(
                        organization.Id,
                        lease.Source.Id,
                        message.Id,
                        cancellationToken);
                    ignoredCount++;
                    continue;
                }

                await indexingService.IndexAsync(
                    organization,
                    lease.Source.Id,
                    lease.Source.Microsoft365Connection.TenantId!,
                    lease.Source.ExternalResourceId,
                    message,
                    cancellationToken);

                if (message.CreatedDateTime is not null
                    && message.LastModifiedDateTime is not null
                    && message.CreatedDateTime == message.LastModifiedDateTime)
                {
                    createdCount++;
                }
                else
                {
                    modifiedCount++;
                }
            }

            finalDeltaLink = page.DeltaLink ?? finalDeltaLink;
        }

        if (string.IsNullOrWhiteSpace(finalDeltaLink))
        {
            throw new InvalidOperationException(
                "Microsoft Graph did not return a final Outlook delta link.");
        }

        var confirmed = await sourceSynchronizationRepository.ConfirmCheckpointAsync(
            lease.Source.Id,
            lease.LeaseId,
            finalDeltaLink,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (!confirmed)
        {
            throw new InvalidOperationException(
                "The Microsoft 365 source synchronization lease was lost.");
        }

        return new Microsoft365SynchronizationCounters(
            createdCount,
            modifiedCount,
            deletedCount,
            ignoredCount,
            FailedCount: 0);
    }

    private DateTimeOffset GetRetentionCutoff() =>
        timeProvider.GetUtcNow().AddDays(-options.Value.OutlookRetentionDays);

    private static bool IsOutsideRetention(
        Microsoft365OutlookMessageDelta message,
        DateTimeOffset retentionCutoff) =>
        message.ReceivedDateTime is not null
        && message.ReceivedDateTime.Value < retentionCutoff;

    private async Task<SynchronizationLease> PrepareLeaseAsync(
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        var source = await outlookRepository.FindForSynchronizationAsync(sourceId, cancellationToken)
            ?? throw new NotFoundException("Microsoft 365 Outlook source was not found.");
        EnsureSourceCanBeSynchronized(source);

        var attemptedAt = timeProvider.GetUtcNow();
        var leaseId = Guid.NewGuid();
        var isAcquired = await sourceSynchronizationRepository.TryAcquireLeaseAsync(
            sourceId,
            leaseId,
            attemptedAt,
            attemptedAt.AddMinutes(options.Value.SynchronizationLeaseMinutes),
            cancellationToken);
        return new SynchronizationLease(source, leaseId, isAcquired);
    }

    private async Task MarkAccessErrorAsync(
        Guid sourceId,
        Guid leaseId,
        string errorCode,
        CancellationToken cancellationToken)
    {
        var marked = await sourceSynchronizationRepository.MarkAccessErrorAsync(
            sourceId,
            leaseId,
            errorCode,
            cancellationToken);
        if (!marked)
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
        string? lastErrorCode,
        CancellationToken cancellationToken)
    {
        var recorded = await sourceSynchronizationRepository.RecordSynchronizationOutcomeAsync(
            sourceId,
            synchronizationId,
            status,
            counters,
            timeProvider.GetUtcNow(),
            lastErrorCode,
            cancellationToken);
        if (!recorded)
        {
            throw new InvalidOperationException(
                "The Microsoft 365 synchronization could not be updated.");
        }
    }

    private static void EnsureSourceCanBeSynchronized(Microsoft365Source source)
    {
        var connection = source.Microsoft365Connection;
        if (source.Kind != Microsoft365SourceKind.OutlookMailbox
            || !source.IsIndexed
            || source.Status is not Microsoft365SourceStatus.Enabled
                and not Microsoft365SourceStatus.FullResyncRequired
            || connection.Status != Microsoft365ConnectionStatus.Active
            || connection.OrganizationConnector.Status != RecordStatus.Active
            || !connection.OrganizationConnector.IsConfigured)
        {
            throw new InvalidOperationException(
                "Only an explicitly enabled Outlook source attached to an active connector can be synchronized.");
        }

        if (string.IsNullOrWhiteSpace(connection.TenantId)
            || string.IsNullOrWhiteSpace(source.ExternalResourceId)
            || string.IsNullOrWhiteSpace(source.ParentExternalResourceId))
        {
            throw new InvalidOperationException(
                "The Outlook source requires a tenant, mailbox user and mail folder identifier.");
        }
    }

    private sealed record SynchronizationLease(
        Microsoft365Source Source,
        Guid LeaseId,
        bool IsAcquired);
}
