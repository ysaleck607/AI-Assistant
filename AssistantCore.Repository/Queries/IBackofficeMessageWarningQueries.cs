namespace AssistantCore.Repository.Queries;

public interface IBackofficeMessageWarningQueries
{
    Task<BackofficeMessageWarningListPageData> SearchAsync(
        Guid? organizationId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        bool contentGapsOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
