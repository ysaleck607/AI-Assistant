using AssistantCore.Repository.Domain.Enums;

namespace AssistantCore.Repository.Queries;

public sealed record OperationalIncidentDetailData(
    Guid Id,
    DateTimeOffset OccurredAt,
    OperationalIncidentSubsystem Subsystem,
    OperationalIncidentSeverity Severity,
    string Summary,
    string SafeDetail,
    Guid? OrganizationId,
    string? OrganizationName,
    Guid? OrganizationMemberId,
    string? RelatedResourceType,
    Guid? RelatedResourceId,
    string CorrelationId,
    OperationalIncidentStatus Status,
    DateTimeOffset? ResolvedAt,
    string? ResolvedByEmail,
    string? ResolutionNotes);
