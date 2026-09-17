using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Commands.GetBackofficeOrganizationUserDetails;

public sealed record GetBackofficeOrganizationUserDetailsQuery(
    Guid OrganizationId,
    Guid UserId) : IRequest<BackofficeUserDetailsDto>;
