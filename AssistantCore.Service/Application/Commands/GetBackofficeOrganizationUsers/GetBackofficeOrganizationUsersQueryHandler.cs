using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Application.Services.Backoffice;

namespace AssistantCore.Service.Application.Commands.GetBackofficeOrganizationUsers;

public sealed class GetBackofficeOrganizationUsersQueryHandler(
    IBackofficeOrganizationService organizationService)
    : IRequestHandler<GetBackofficeOrganizationUsersQuery, BackofficeUserListResponse>
{
    public Task<BackofficeUserListResponse> HandleAsync(
        GetBackofficeOrganizationUsersQuery request,
        CancellationToken cancellationToken) =>
        organizationService.GetUsersAsync(request.OrganizationId, request.Search, cancellationToken);
}
