using AssistantCore.Repository.Queries;

namespace AssistantCore.Service.Application.Models.Backoffice;

public sealed record BackofficeIncidentSummaryDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Subsystem,
    string Severity,
    string Summary,
    Guid? OrganizationId,
    string? OrganizationName,
    Guid? OrganizationMemberId,
    string? RelatedResourceType,
    Guid? RelatedResourceId,
    string CorrelationId,
    string Status)
{
    public static BackofficeIncidentSummaryDto FromData(OperationalIncidentSummaryData data) => new(
        data.Id,
        data.OccurredAt,
        data.Subsystem.ToString(),
        data.Severity.ToString(),
        data.Summary,
        data.OrganizationId,
        data.OrganizationName,
        data.OrganizationMemberId,
        data.RelatedResourceType,
        data.RelatedResourceId,
        data.CorrelationId,
        data.Status.ToString());
}
