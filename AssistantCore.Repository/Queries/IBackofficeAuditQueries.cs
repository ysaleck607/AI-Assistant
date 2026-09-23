namespace AssistantCore.Repository.Queries;

public interface IBackofficeAuditQueries
{
    Task<BackofficeAuditListPageData> SearchAsync(
        Guid? organizationId,
        Guid? actorId,
        string? action,
        string? result,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
