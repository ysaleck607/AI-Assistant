using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;

namespace AssistantCore.Repository.Repositories;

/// <summary>
/// Execution reprenable de la purge physique des conversations supprimees.
/// Chaque etape confirmee est persistee : un arret brutal reprend a la derniere
/// etape reussie au lieu de rejouer les precedentes.
/// </summary>
public interface IConversationPurgeRepository
{
    /// <summary>
    /// Reclame atomiquement une demande echue. Une demande dont le bail a expire
    /// redevient reclamable : un worker mort ne bloque jamais une purge.
    /// Retourne null lorsqu'il n'y a rien a traiter.
    /// </summary>
    Task<ConversationPurgeRequest?> ClaimNextAsync(
        Guid leaseId,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Supprime les sources et avertissements des messages de la conversation.
    /// Retourne le nombre de sources supprimees. L'operation est idempotente :
    /// rejouee, elle n'en trouve plus et retourne zero.
    /// </summary>
    Task<int> DeleteMessageSourcesAsync(
        Guid purgeRequestId,
        Guid leaseId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Supprime les messages de la conversation et retourne leur nombre.
    /// </summary>
    Task<int> DeleteMessagesAsync(
        Guid purgeRequestId,
        Guid leaseId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Supprime la conversation elle-meme. La demande de purge survit a cette
    /// suppression afin de conserver la preuve.
    /// </summary>
    Task DeleteConversationAsync(
        Guid purgeRequestId,
        Guid leaseId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifie que plus aucune ligne ne subsiste pour cette conversation.
    /// Le statut Completed n'est enregistre qu'apres cette verification.
    /// </summary>
    Task<bool> VerifyPurgedAsync(
        Guid purgeRequestId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enregistre la reussite d'une etape et libere le bail.
    /// </summary>
    Task AdvanceAsync(
        Guid purgeRequestId,
        Guid leaseId,
        ConversationPurgeStep nextStep,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        Guid purgeRequestId,
        Guid leaseId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enregistre un echec. Un echec temporaire est reprogramme avec un delai
    /// croissant; au-dela du nombre maximal de tentatives il devient permanent
    /// et reste visible pour alerte, sans etre rejoue indefiniment.
    /// </summary>
    Task FailAsync(
        Guid purgeRequestId,
        Guid leaseId,
        string errorCode,
        bool isPermanent,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken = default);
}
