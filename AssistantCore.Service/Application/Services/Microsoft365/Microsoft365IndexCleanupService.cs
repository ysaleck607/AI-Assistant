using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Models.Microsoft365;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public sealed class Microsoft365IndexCleanupService(
    IMicrosoft365IndexedContentRepository indexedContentRepository,
    IMicrosoft365PassageIndexWriter indexWriter,
    IMicrosoft365SourceSynchronizationRepository synchronizationRepository,
    TimeProvider timeProvider) : IMicrosoft365IndexCleanupService
{
    public async Task CleanupAsync(
        Guid organizationId,
        Guid sourceId,
        Guid synchronizationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var contents = await indexedContentRepository.GetBySourceAsync(
                organizationId,
                sourceId,
                cancellationToken);
            var chunkIds = contents
                .SelectMany(content => content.Passages)
                .Select(passage => passage.ChunkId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (chunkIds.Length > 0)
            {
                await indexWriter.DeleteAsync(organizationId, chunkIds, cancellationToken);
            }

            foreach (var content in contents)
            {
                await indexedContentRepository.DeleteAsync(content, cancellationToken);
            }

            await RecordOutcomeAsync(
                sourceId,
                synchronizationId,
                Microsoft365SynchronizationStatus.Succeeded,
                lastErrorCode: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RecordOutcomeAsync(
                sourceId,
                synchronizationId,
                Microsoft365SynchronizationStatus.Cancelled,
                "Microsoft365IndexCleanupCancelled");
            throw;
        }
        catch
        {
            await RecordOutcomeAsync(
                sourceId,
                synchronizationId,
                Microsoft365SynchronizationStatus.TemporaryFailure,
                "Microsoft365IndexCleanupFailed");
            throw;
        }
    }

    private async Task RecordOutcomeAsync(
        Guid sourceId,
        Guid synchronizationId,
        Microsoft365SynchronizationStatus status,
        string? lastErrorCode)
    {
        var recorded = await synchronizationRepository.RecordSynchronizationOutcomeAsync(
            sourceId,
            synchronizationId,
            status,
            status == Microsoft365SynchronizationStatus.Succeeded
                ? Microsoft365SynchronizationCounters.Empty
                : Microsoft365SynchronizationCounters.Empty with { FailedCount = 1 },
            timeProvider.GetUtcNow(),
            lastErrorCode,
            CancellationToken.None);
        if (!recorded)
        {
            throw new InvalidOperationException(
                "The Microsoft 365 index cleanup outcome could not be recorded.");
        }
    }
}
