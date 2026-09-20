using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;

namespace AssistantCore.Repository.Repositories.Incidents;

/// <summary>
/// Construit l'entite a partir de valeurs deja redigees : ce projet ne depend d'aucune
/// couche superieure, la redaction (AssistantCore.Service) doit avoir eu lieu avant l'appel.
/// </summary>
public static class OperationalIncidentFactory
{
    public static OperationalIncident Create(
        OperationalIncidentSubsystem subsystem,
        OperationalIncidentSeverity severity,
        string correlationId,
        Guid? organizationId,
        Guid? organizationMemberId,
        string summary,
        string safeDetail,
        string? relatedResourceType,
        Guid? relatedResourceId,
        DateTimeOffset occurredAt)
    {
        return new OperationalIncident
        {
            Id = Guid.NewGuid(),
            OccurredAt = occurredAt,
            Subsystem = subsystem,
            Severity = severity,
            CorrelationId = correlationId,
            OrganizationId = organizationId,
            OrganizationMemberId = organizationMemberId,
            Summary = summary,
            SafeDetail = safeDetail,
            RelatedResourceType = relatedResourceType,
            RelatedResourceId = relatedResourceId,
            Status = OperationalIncidentStatus.Open
        };
    }
}
