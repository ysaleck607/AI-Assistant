using AssistantCore.Repository.Domain.Enums;

namespace AssistantCore.Repository.Domain.Entities;

/// <summary>
/// Operation de purge generique, reutilisable par toute categorie de retention autre
/// que Conversation (qui dispose deja de ConversationPurgeRequest). Le couple
/// (OrganizationId, Scope, TargetId) identifie de facon unique la cible purgee :
/// deux demandes identiques n'ouvrent jamais deux operations.
/// </summary>
public sealed class PurgeOperation
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public PurgeOperationScope Scope { get; set; }

    /// <summary>
    /// Identifiant de l'element purge dans sa propre table (TokenConsumption,
    /// Microsoft365IndexedContent, Microsoft365DocumentWork, etc. selon Scope).
    /// </summary>
    public Guid TargetId { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset PurgeAfter { get; set; }

    public PurgeOperationStatus Status { get; set; }

    /// <summary>
    /// Derniere etape confirmee. Les etapes sont propres a chaque Scope ; une reprise
    /// repart d'ici plutot que du debut.
    /// </summary>
    public string Step { get; set; } = string.Empty;

    /// <summary>
    /// Bail empechant deux workers de traiter la meme operation. Un bail expire
    /// rend l'operation a nouveau reclamable : un worker mort ne bloque rien.
    /// </summary>
    public Guid? LeaseId { get; set; }

    public DateTimeOffset? LeaseExpiresAt { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public string? LastErrorCode { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
