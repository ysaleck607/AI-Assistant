using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Commands.RequestMicrosoft365Reindex;

public sealed record RequestMicrosoft365ReindexCommand(
    Guid OrganizationId,
    string? Reason) : IRequest<BackofficeMicrosoft365ReindexResponse>;
