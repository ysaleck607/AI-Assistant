using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Commands.GetBackofficeOrganizationUsers;

public sealed record GetBackofficeOrganizationUsersQuery(
    Guid OrganizationId,
    string? Search) : IRequest<BackofficeUserListResponse>;
