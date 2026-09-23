namespace AssistantCore.Repository.Queries;

public sealed record OperationalIncidentListPageData(
    IReadOnlyCollection<OperationalIncidentSummaryData> Items,
    int Page,
    int PageSize,
    int TotalCount);
