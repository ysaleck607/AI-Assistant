using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Services.Backoffice;

public interface IBackofficeAuditService
{
    Task<BackofficeAuditListResponse> SearchAuditEntriesAsync(
        int page,
        int pageSize,
        Guid? organizationId,
        Guid? actorId,
        string? action,
        string? result,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default);
}
