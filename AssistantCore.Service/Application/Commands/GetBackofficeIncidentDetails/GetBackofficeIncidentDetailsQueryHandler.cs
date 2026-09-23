using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Application.Services.Backoffice;

namespace AssistantCore.Service.Application.Commands.GetBackofficeIncidentDetails;

public sealed class GetBackofficeIncidentDetailsQueryHandler(IBackofficeIncidentService incidentService)
    : IRequestHandler<GetBackofficeIncidentDetailsQuery, BackofficeIncidentDetailDto>
{
    public async Task<BackofficeIncidentDetailDto> HandleAsync(
        GetBackofficeIncidentDetailsQuery request,
        CancellationToken cancellationToken)
    {
        return await incidentService.GetIncidentDetailsAsync(request.IncidentId, cancellationToken);
    }
}
