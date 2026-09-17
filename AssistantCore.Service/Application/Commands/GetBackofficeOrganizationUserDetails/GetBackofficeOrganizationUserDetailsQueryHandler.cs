using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Application.Services.Backoffice;

namespace AssistantCore.Service.Application.Commands.GetBackofficeOrganizationUserDetails;

public sealed class GetBackofficeOrganizationUserDetailsQueryHandler(
    IBackofficeOrganizationService organizationService)
    : IRequestHandler<GetBackofficeOrganizationUserDetailsQuery, BackofficeUserDetailsDto>
{
    public Task<BackofficeUserDetailsDto> HandleAsync(
        GetBackofficeOrganizationUserDetailsQuery request,
        CancellationToken cancellationToken) =>
        organizationService.GetUserDetailsAsync(request.OrganizationId, request.UserId, cancellationToken);
}
