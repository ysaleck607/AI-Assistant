using AssistantCore.Repository.Domain.Enums;

namespace AssistantCore.Repository.Domain.Entities;

public sealed class ConversationPurgeRequest
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public Guid OrganizationId { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset PurgeAfter { get; set; }

    public ConversationPurgeStatus Status { get; set; }

    /// <summary>
    /// Derniere etape confirmee. Une reprise repart d'ici plutot que du debut.
    /// </summary>
    public ConversationPurgeStep Step { get; set; }

    /// <summary>
    /// Bail empechant deux workers de traiter la meme demande. Un bail expire
    /// rend la demande a nouveau reclamable : un worker mort ne bloque rien.
    /// </summary>
    public Guid? LeaseId { get; set; }

    public DateTimeOffset? LeaseExpiresAt { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public string? LastErrorCode { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Preuve de la purge : des nombres, jamais le contenu supprime.
    /// </summary>
    public int DeletedMessageCount { get; set; }

    public int DeletedSourceCount { get; set; }
}
