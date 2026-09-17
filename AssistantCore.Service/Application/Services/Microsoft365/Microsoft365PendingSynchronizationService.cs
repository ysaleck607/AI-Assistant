using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Models.Microsoft365;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public sealed class Microsoft365PendingSynchronizationService(
    IMicrosoft365PendingSynchronizationRepository repository,
    IMicrosoft365DriveSynchronizationService driveSynchronizationService,
    IMicrosoft365ListSynchronizationService listSynchronizationService,
    IMicrosoft365OutlookSynchronizationService outlookSynchronizationService,
    IMicrosoft365IndexCleanupService indexCleanupService,
    IMicrosoft365SourceSynchronizationRepository synchronizationRepository,
    TimeProvider timeProvider) : IMicrosoft365PendingSynchronizationService
{
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        var work = await repository.ClaimNextAsync(
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (work is null)
        {
            return false;
        }

        if (work.Type == Microsoft365SynchronizationType.IndexCleanup)
        {
            await indexCleanupService.CleanupAsync(
                work.OrganizationId,
                work.SourceId,
                work.SynchronizationId,
                cancellationToken);
            return true;
        }

        if (!work.IsIndexed
            || work.SourceStatus is not Microsoft365SourceStatus.Enabled
                and not Microsoft365SourceStatus.FullResyncRequired)
        {
            var cancelled = await synchronizationRepository.RecordSynchronizationOutcomeAsync(
                work.SourceId,
                work.SynchronizationId,
                Microsoft365SynchronizationStatus.Cancelled,
                Microsoft365SynchronizationCounters.Empty,
                timeProvider.GetUtcNow(),
                "Microsoft365SourceNoLongerIndexable",
                CancellationToken.None);
            if (!cancelled)
            {
                throw new InvalidOperationException(
                    "The obsolete Microsoft 365 synchronization could not be cancelled.");
            }
            return true;
        }

        switch (work.SourceKind)
        {
            case Microsoft365SourceKind.SharePointList:
                if (work.Type == Microsoft365SynchronizationType.Initial)
                {
                    await listSynchronizationService.StartInitialSynchronizationAsync(
                        work.SourceId,
                        work.SynchronizationId,
                        cancellationToken);
                }
                else
                {
                    await listSynchronizationService.StartDeltaSynchronizationAsync(
                        work.SourceId,
                        work.SynchronizationId,
                        cancellationToken);
                }
                break;

            case Microsoft365SourceKind.SharePointDrive:
            case Microsoft365SourceKind.OneDrive:
                if (work.Type == Microsoft365SynchronizationType.Initial)
                {
                    await driveSynchronizationService.StartInitialSynchronizationAsync(
                        work.SourceId,
                        work.SynchronizationId,
                        work.ReindexOperationId,
                        cancellationToken);
                }
                else
                {
                    await driveSynchronizationService.StartDeltaSynchronizationAsync(
                        work.SourceId,
                        work.SynchronizationId,
                        cancellationToken);
                }
                break;

            case Microsoft365SourceKind.OutlookMailbox:
                if (work.Type == Microsoft365SynchronizationType.Initial)
                {
                    await outlookSynchronizationService.StartInitialSynchronizationAsync(
                        work.SourceId,
                        work.SynchronizationId,
                        cancellationToken);
                }
                else
                {
                    await outlookSynchronizationService.StartDeltaSynchronizationAsync(
                        work.SourceId,
                        work.SynchronizationId,
                        cancellationToken);
                }
                break;

            default:
                throw new InvalidOperationException(
                    $"Microsoft 365 source kind '{work.SourceKind}' cannot use the ingestion synchronization pipeline.");
        }

        return true;
    }
}
