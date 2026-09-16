using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Commands.GetMicrosoft365ReindexStatus;

public sealed record GetMicrosoft365ReindexStatusQuery(
    Guid OrganizationId,
    Guid OperationId) : IRequest<BackofficeMicrosoft365ReindexStatusResponse>;
