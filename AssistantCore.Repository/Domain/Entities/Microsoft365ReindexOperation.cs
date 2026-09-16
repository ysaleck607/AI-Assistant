using AssistantCore.Repository.Domain.Enums;

namespace AssistantCore.Repository.Domain.Entities;

/// <summary>
/// Reprise complete demandee par un operateur Synaptix sur les bibliotheques SharePoint
/// activees d'une organisation. Elle regroupe les synchronisations completes creees pour
/// l'occasion et conserve l'identite du demandeur, les compteurs et les dates du suivi.
/// </summary>
public sealed class Microsoft365ReindexOperation
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid Microsoft365ConnectionId { get; set; }

    public Guid RequestedByOperatorId { get; set; }

    public string? Reason { get; set; }

    public Microsoft365ReindexOperationStatus Status { get; set; }

    public int SourceCount { get; set; }

    public int CompletedSourceCount { get; set; }

    public int DiscoveredDocumentCount { get; set; }

    public int ProcessedDocumentCount { get; set; }

    public int IgnoredDocumentCount { get; set; }

    public int FailedDocumentCount { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? LastErrorCode { get; set; }

    public Organization Organization { get; set; } = null!;

    public Microsoft365Connection Microsoft365Connection { get; set; } = null!;

    public ICollection<Microsoft365Synchronization> Synchronizations { get; set; } = [];
}
