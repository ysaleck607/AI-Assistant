namespace AssistantCore.Repository.Queries;

public interface IBackofficeMessageWarningQueries
{
    Task<BackofficeMessageWarningListPageData> SearchAsync(
        Guid? organizationId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
