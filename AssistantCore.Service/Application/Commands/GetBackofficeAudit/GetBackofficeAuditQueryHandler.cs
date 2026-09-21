using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Application.Services.Backoffice;

namespace AssistantCore.Service.Application.Commands.GetBackofficeAudit;

public sealed class GetBackofficeAuditQueryHandler(IBackofficeAuditService auditService)
    : IRequestHandler<GetBackofficeAuditQuery, BackofficeAuditListResponse>
{
    public async Task<BackofficeAuditListResponse> HandleAsync(
        GetBackofficeAuditQuery request,
        CancellationToken cancellationToken)
    {
        return await auditService.SearchAuditEntriesAsync(
            request.Page,
            request.PageSize,
            request.OrganizationId,
            request.ActorId,
            request.Action,
            request.Result,
            request.From,
            request.To,
            cancellationToken);
    }
}
