using AssistantCore.Repository.Domain.Enums;

namespace AssistantCore.Repository.Queries;

public sealed record OperationalIncidentSummaryData(
    Guid Id,
    DateTimeOffset OccurredAt,
    OperationalIncidentSubsystem Subsystem,
    OperationalIncidentSeverity Severity,
    string Summary,
    Guid? OrganizationId,
    string? OrganizationName,
    Guid? OrganizationMemberId,
    string? RelatedResourceType,
    Guid? RelatedResourceId,
    string CorrelationId,
    OperationalIncidentStatus Status);
