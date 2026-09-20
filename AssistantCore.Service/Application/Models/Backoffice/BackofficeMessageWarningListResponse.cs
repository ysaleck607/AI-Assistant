using AssistantCore.Repository.Queries;

namespace AssistantCore.Service.Application.Models.Backoffice;

public sealed record BackofficeMessageWarningListResponse(
    IReadOnlyCollection<BackofficeMessageWarningSummaryDto> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public static BackofficeMessageWarningListResponse FromData(BackofficeMessageWarningListPageData data) => new(
        data.Items.Select(BackofficeMessageWarningSummaryDto.FromData).ToList(),
        data.Page,
        data.PageSize,
        data.TotalCount);
}
