using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Application.Services.Backoffice;

namespace AssistantCore.Service.Application.Commands.GetBackofficeIncidents;

public sealed class GetBackofficeIncidentsQueryHandler(IBackofficeIncidentService incidentService)
    : IRequestHandler<GetBackofficeIncidentsQuery, BackofficeIncidentListResponse>
{
    public async Task<BackofficeIncidentListResponse> HandleAsync(
        GetBackofficeIncidentsQuery request,
        CancellationToken cancellationToken)
    {
        return await incidentService.SearchIncidentsAsync(
            request.Page,
            request.PageSize,
            request.OrganizationId,
            request.From,
            request.To,
            request.Subsystem,
            request.Severity,
            request.CorrelationId,
            cancellationToken);
    }
}
