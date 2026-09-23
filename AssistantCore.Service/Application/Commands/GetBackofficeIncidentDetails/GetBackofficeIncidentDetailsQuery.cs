using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Commands.GetBackofficeIncidentDetails;

public sealed record GetBackofficeIncidentDetailsQuery(Guid IncidentId) : IRequest<BackofficeIncidentDetailDto>;
