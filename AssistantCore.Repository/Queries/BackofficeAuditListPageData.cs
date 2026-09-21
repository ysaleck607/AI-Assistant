namespace AssistantCore.Repository.Queries;

public sealed record BackofficeAuditListPageData(
    IReadOnlyCollection<BackofficeAuditEntryData> Items,
    int Page,
    int PageSize,
    int TotalCount);
