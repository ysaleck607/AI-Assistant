using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Commands.ReevaluateBackofficeUserAccess;

public sealed record ReevaluateBackofficeUserAccessCommand(
    Guid OrganizationId,
    Guid UserId) : IRequest<BackofficeAccessReevaluationResultDto>;
