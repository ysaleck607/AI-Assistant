using AssistantCore.Repository.Queries;

namespace AssistantCore.Service.Application.Models.Backoffice;

public sealed record BackofficeIncidentListResponse(
    IReadOnlyCollection<BackofficeIncidentSummaryDto> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public static BackofficeIncidentListResponse FromData(OperationalIncidentListPageData data) => new(
        data.Items.Select(BackofficeIncidentSummaryDto.FromData).ToList(),
        data.Page,
        data.PageSize,
        data.TotalCount);
}
