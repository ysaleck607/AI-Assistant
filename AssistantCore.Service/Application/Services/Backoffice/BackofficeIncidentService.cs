using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Queries;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Services.Backoffice;

public sealed class BackofficeIncidentService(IOperationalIncidentQueries incidentQueries)
    : IBackofficeIncidentService
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 25;
    private const int MaximumPageSize = 100;

    public async Task<BackofficeIncidentListResponse> SearchIncidentsAsync(
        int page,
        int pageSize,
        Guid? organizationId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        OperationalIncidentSubsystem? subsystem,
        OperationalIncidentSeverity? severity,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = page <= 0 ? DefaultPage : page;
        var normalizedPageSize = pageSize <= 0
            ? DefaultPageSize
            : Math.Min(pageSize, MaximumPageSize);

        var result = await incidentQueries.SearchAsync(
            organizationId,
            from,
            to,
            subsystem,
            severity,
            correlationId,
            normalizedPage,
            normalizedPageSize,
            cancellationToken);

        return BackofficeIncidentListResponse.FromData(result);
    }

    public async Task<BackofficeIncidentDetailDto> GetIncidentDetailsAsync(
        Guid incidentId,
        CancellationToken cancellationToken = default)
    {
        var data = await incidentQueries.GetByIdAsync(incidentId, cancellationToken);
        return data is null
            ? throw new NotFoundException("Incident not found.")
            : BackofficeIncidentDetailDto.FromData(data);
    }
}
