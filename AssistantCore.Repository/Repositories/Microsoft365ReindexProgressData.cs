using AssistantCore.Repository.Domain.Enums;

namespace AssistantCore.Repository.Repositories;

/// <summary>
/// Etat brut d'une reprise complete : une ligne par bibliotheque reprise et le decompte
/// des travaux documentaires qu'elle a produits. Le calcul de l'etat global appartient a
/// la couche applicative.
/// </summary>
public sealed record Microsoft365ReindexProgressData(
    IReadOnlyCollection<Microsoft365ReindexSynchronizationState> Synchronizations,
    Microsoft365ReindexDocumentCounts Documents);

public sealed record Microsoft365ReindexSynchronizationState(
    Guid SynchronizationId,
    Guid SourceId,
    Microsoft365SynchronizationStatus Status,
    int IgnoredCount,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? LastErrorCode);

public sealed record Microsoft365ReindexDocumentCounts(
    int DiscoveredCount,
    int ProcessedCount,
    int FailedCount,
    int UnfinishedCount)
{
    public static Microsoft365ReindexDocumentCounts Empty { get; } = new(0, 0, 0, 0);
}
