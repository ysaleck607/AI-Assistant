using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;

namespace AssistantCore.Repository.Repositories;

/// <summary>
/// Persistance generique des operations de purge reprenables, pour toute categorie
/// de retention autre que Conversation (deja servie par IConversationPurgeRepository).
/// Ne connait aucune etape metier propre a une categorie : elle ne fait que creer,
/// reclamer et faire progresser une operation identifiee par (OrganizationId, Scope,
/// TargetId).
/// </summary>
public interface IPurgeOperationRepository
{
    /// <summary>
    /// Cree une operation de purge, ou retourne celle deja ouverte pour la meme
    /// (OrganizationId, Scope, TargetId). Deux demandes identiques, concurrentes ou
    /// non, n'ouvrent jamais deux operations.
    /// </summary>
    Task<PurgeOperation> RequestAsync(
        Guid organizationId,
        PurgeOperationScope scope,
        Guid targetId,
        DateTimeOffset requestedAt,
        DateTimeOffset purgeAfter,
        string initialStep,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reclame atomiquement une operation echue, toutes categories confondues. Une
    /// operation dont le bail a expire redevient reclamable : un worker mort ne
    /// bloque jamais une purge. Retourne null lorsqu'il n'y a rien a traiter.
    /// </summary>
    Task<PurgeOperation?> ClaimNextAsync(
        Guid leaseId,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enregistre la reussite d'une etape et le curseur de reprise, sans liberer le
    /// bail : l'appelant reste responsable de l'operation jusqu'a CompleteAsync ou
    /// FailAsync.
    /// </summary>
    Task AdvanceAsync(
        Guid operationId,
        Guid leaseId,
        string nextStep,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        Guid operationId,
        Guid leaseId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enregistre un echec. Un echec temporaire est reprogramme avec un delai
    /// croissant; au-dela du nombre maximal de tentatives il devient permanent
    /// et reste visible pour alerte, sans etre rejoue indefiniment.
    /// </summary>
    Task FailAsync(
        Guid operationId,
        Guid leaseId,
        string errorCode,
        bool isPermanent,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken = default);
}
