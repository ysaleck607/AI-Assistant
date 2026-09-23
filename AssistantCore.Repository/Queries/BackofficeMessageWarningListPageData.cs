namespace AssistantCore.Repository.Queries;

public sealed record BackofficeMessageWarningListPageData(
    IReadOnlyCollection<BackofficeMessageWarningSummaryData> Items,
    int Page,
    int PageSize,
    int TotalCount);
