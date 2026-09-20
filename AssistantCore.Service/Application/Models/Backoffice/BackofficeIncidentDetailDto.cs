using AssistantCore.Repository.Queries;

namespace AssistantCore.Service.Application.Models.Backoffice;

public sealed record BackofficeIncidentDetailDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Subsystem,
    string Severity,
    string Summary,
    string SafeDetail,
    Guid? OrganizationId,
    string? OrganizationName,
    Guid? OrganizationMemberId,
    string? RelatedResourceType,
    Guid? RelatedResourceId,
    string CorrelationId,
    string Status,
    DateTimeOffset? ResolvedAt,
    string? ResolvedByEmail,
    string? ResolutionNotes,
    string CentralLogsHint)
{
    public static BackofficeIncidentDetailDto FromData(OperationalIncidentDetailData data) => new(
        data.Id,
        data.OccurredAt,
        data.Subsystem.ToString(),
        data.Severity.ToString(),
        data.Summary,
        data.SafeDetail,
        data.OrganizationId,
        data.OrganizationName,
        data.OrganizationMemberId,
        data.RelatedResourceType,
        data.RelatedResourceId,
        data.CorrelationId,
        data.Status.ToString(),
        data.ResolvedAt,
        data.ResolvedByEmail,
        data.ResolutionNotes,
        BuildCentralLogsHint(data.CorrelationId));

    // Pas de SDK Azure Monitor Query cable ce soir : on fournit un fragment de requete
    // copiable plutot qu'un lien direct.
    private static string BuildCentralLogsHint(string correlationId) =>
        $"CorrelationId:\"{correlationId}\"";
}
