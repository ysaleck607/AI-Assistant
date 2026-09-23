using AssistantCore.Repository.Queries;
using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Services.Backoffice;

public sealed class BackofficeAuditService(IBackofficeAuditQueries auditQueries) : IBackofficeAuditService
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 25;
    private const int MaximumPageSize = 100;

    public async Task<BackofficeAuditListResponse> SearchAuditEntriesAsync(
        int page,
        int pageSize,
        Guid? organizationId,
        Guid? actorId,
        string? action,
        string? resultFilter,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = page <= 0 ? DefaultPage : page;
        var normalizedPageSize = pageSize <= 0
            ? DefaultPageSize
            : Math.Min(pageSize, MaximumPageSize);

        var pageData = await auditQueries.SearchAsync(
            organizationId,
            actorId,
            action,
            resultFilter,
            from,
            to,
            normalizedPage,
            normalizedPageSize,
            cancellationToken);

        return BackofficeAuditListResponse.FromData(pageData);
    }
}
