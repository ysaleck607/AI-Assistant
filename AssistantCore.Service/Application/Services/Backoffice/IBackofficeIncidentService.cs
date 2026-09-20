using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Services.Backoffice;

public interface IBackofficeIncidentService
{
    Task<BackofficeIncidentListResponse> SearchIncidentsAsync(
        int page,
        int pageSize,
        Guid? organizationId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        OperationalIncidentSubsystem? subsystem,
        OperationalIncidentSeverity? severity,
        string? correlationId,
        CancellationToken cancellationToken = default);

    Task<BackofficeIncidentDetailDto> GetIncidentDetailsAsync(
        Guid incidentId,
        CancellationToken cancellationToken = default);
}
