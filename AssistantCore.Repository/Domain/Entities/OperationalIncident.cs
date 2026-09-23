using AssistantCore.Repository.Domain.Enums;

namespace AssistantCore.Repository.Domain.Entities;

/// <summary>
/// Incident operationnel redige de facon securisee (jamais de token, secret ou stack trace
/// brute) pour permettre au support de diagnostiquer une panne de sous-systeme depuis le
/// backoffice sans ouvrir Splunk/Azure.
/// </summary>
public sealed class OperationalIncident
{
    public Guid Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public OperationalIncidentSubsystem Subsystem { get; set; }

    public OperationalIncidentSeverity Severity { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    // Nullable : certains incidents (demarrage worker, panne Foundry globale) ne sont pas
    // rattachables a une organisation precise.
    public Guid? OrganizationId { get; set; }

    public Guid? OrganizationMemberId { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string SafeDetail { get; set; } = string.Empty;

    public string? RelatedResourceType { get; set; }

    public Guid? RelatedResourceId { get; set; }

    public OperationalIncidentStatus Status { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public string? ResolvedByEmail { get; set; }

    public string? ResolutionNotes { get; set; }

    public Organization? Organization { get; set; }
}
