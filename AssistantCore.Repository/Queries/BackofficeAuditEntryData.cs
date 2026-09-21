using AssistantCore.Repository.Domain.Enums;

namespace AssistantCore.Repository.Queries;

public sealed record BackofficeAuditEntryData(
    Guid Id,
    DateTimeOffset OccurredAt,
    AdministrativeAuditAction Action,
    Guid ActorId,
    Guid OrganizationId,
    string OrganizationName,
    string TargetType,
    Guid TargetId,
    string CorrelationId,
    string OldValuesJson,
    string NewValuesJson);
