using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;

namespace AssistantCore.Service.Application.Services.Microsoft365;

/// <summary>
/// Etat global d'une reprise, deduit de ses synchronisations et de ses travaux documentaires.
/// </summary>
public sealed record Microsoft365ReindexProgress(
    Microsoft365ReindexOperationStatus Status,
    int CompletedSourceCount,
    int DiscoveredDocumentCount,
    int ProcessedDocumentCount,
    int IgnoredDocumentCount,
    int FailedDocumentCount,
    DateTimeOffset? StartedAt,
    bool IsFinished,
    string? LastErrorCode);

/// <summary>
/// Traduit l'etat des synchronisations d'une reprise en etat global publie a l'interface interne.
/// </summary>
public static class Microsoft365ReindexProgressCalculator
{
    public static Microsoft365ReindexProgress Calculate(Microsoft365ReindexProgressData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var synchronizations = data.Synchronizations;
        var finishedSynchronizations = synchronizations.Where(IsFinished).ToArray();
        var failedSynchronizations = synchronizations.Where(IsFailed).ToArray();
        var startedAt = synchronizations
            .Where(synchronization => synchronization.StartedAt is not null)
            .Select(synchronization => synchronization.StartedAt!.Value)
            .DefaultIfEmpty()
            .Min();

        var allSynchronizationsFinished = synchronizations.Count > 0
            && finishedSynchronizations.Length == synchronizations.Count;
        var isFinished = allSynchronizationsFinished && data.Documents.UnfinishedCount == 0;

        return new Microsoft365ReindexProgress(
            DetermineStatus(synchronizations, failedSynchronizations.Length > 0, isFinished),
            finishedSynchronizations.Length,
            data.Documents.DiscoveredCount,
            data.Documents.ProcessedCount,
            synchronizations.Sum(synchronization => synchronization.IgnoredCount),
            data.Documents.FailedCount,
            startedAt == default ? null : startedAt,
            isFinished,
            failedSynchronizations
                .Select(synchronization => synchronization.LastErrorCode)
                .FirstOrDefault(errorCode => !string.IsNullOrWhiteSpace(errorCode)));
    }

    private static Microsoft365ReindexOperationStatus DetermineStatus(
        IReadOnlyCollection<Microsoft365ReindexSynchronizationState> synchronizations,
        bool hasFailedSynchronization,
        bool isFinished)
    {
        if (isFinished)
        {
            // Un echec documentaire isole reste visible dans les compteurs sans faire echouer
            // la reprise; seule une bibliotheque entierement perdue la marque en echec.
            return hasFailedSynchronization
                ? Microsoft365ReindexOperationStatus.PermanentFailure
                : Microsoft365ReindexOperationStatus.Succeeded;
        }

        if (synchronizations.Any(synchronization =>
                synchronization.Status == Microsoft365SynchronizationStatus.TemporaryFailure))
        {
            return Microsoft365ReindexOperationStatus.TemporaryFailure;
        }

        return synchronizations.Any(synchronization => synchronization.StartedAt is not null)
            ? Microsoft365ReindexOperationStatus.Running
            : Microsoft365ReindexOperationStatus.Pending;
    }

    private static bool IsFinished(Microsoft365ReindexSynchronizationState synchronization) =>
        synchronization.Status is Microsoft365SynchronizationStatus.Succeeded
            or Microsoft365SynchronizationStatus.PermanentFailure
            or Microsoft365SynchronizationStatus.Cancelled;

    private static bool IsFailed(Microsoft365ReindexSynchronizationState synchronization) =>
        synchronization.Status is Microsoft365SynchronizationStatus.PermanentFailure
            or Microsoft365SynchronizationStatus.Cancelled;
}
