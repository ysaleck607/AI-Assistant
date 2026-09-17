using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Application.Services.Backoffice;

namespace AssistantCore.Service.Application.Commands.ReevaluateBackofficeUserAccess;

public sealed class ReevaluateBackofficeUserAccessCommandHandler(
    IBackofficeOrganizationService organizationService)
    : IRequestHandler<ReevaluateBackofficeUserAccessCommand, BackofficeAccessReevaluationResultDto>
{
    public Task<BackofficeAccessReevaluationResultDto> HandleAsync(
        ReevaluateBackofficeUserAccessCommand request,
        CancellationToken cancellationToken) =>
        organizationService.ReevaluateUserAccessAsync(request.OrganizationId, request.UserId, cancellationToken);
}
