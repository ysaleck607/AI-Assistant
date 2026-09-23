using AssistantCore.Repository.Queries;

namespace AssistantCore.Service.Application.Models.Backoffice;

public sealed record BackofficeAuditListResponse(
    IReadOnlyCollection<BackofficeAuditEntryDto> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public static BackofficeAuditListResponse FromData(BackofficeAuditListPageData data) => new(
        data.Items.Select(BackofficeAuditEntryDto.FromData).ToList(),
        data.Page,
        data.PageSize,
        data.TotalCount);
}
