using AssistantCore.Repository.Domain.Enums;

namespace AssistantCore.Repository.Queries;

public interface IOperationalIncidentQueries
{
    Task<OperationalIncidentListPageData> SearchAsync(
        Guid? organizationId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        OperationalIncidentSubsystem? subsystem,
        OperationalIncidentSeverity? severity,
        string? correlationId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<OperationalIncidentDetailData?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
