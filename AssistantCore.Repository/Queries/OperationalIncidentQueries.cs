using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Repository.Queries;

public sealed class OperationalIncidentQueries(AssistantCoreDbContext dbContext)
    : IOperationalIncidentQueries
{
    public async Task<OperationalIncidentListPageData> SearchAsync(
        Guid? organizationId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        OperationalIncidentSubsystem? subsystem,
        OperationalIncidentSeverity? severity,
        string? correlationId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var incidents = ApplyFilters(
            dbContext.OperationalIncidents.AsNoTracking(),
            organizationId,
            from,
            to,
            subsystem,
            severity,
            correlationId);

        var totalCount = await incidents.CountAsync(cancellationToken);
        var items = await incidents
            .OrderByDescending(incident => incident.OccurredAt)
            .ThenBy(incident => incident.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(incident => new OperationalIncidentSummaryData(
                incident.Id,
                incident.OccurredAt,
                incident.Subsystem,
                incident.Severity,
                incident.Summary,
                incident.OrganizationId,
                incident.Organization != null ? incident.Organization.Name : null,
                incident.OrganizationMemberId,
                incident.RelatedResourceType,
                incident.RelatedResourceId,
                incident.CorrelationId,
                incident.Status))
            .ToListAsync(cancellationToken);

        return new OperationalIncidentListPageData(items, page, pageSize, totalCount);
    }

    public async Task<OperationalIncidentDetailData?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.OperationalIncidents
            .AsNoTracking()
            .Where(incident => incident.Id == id)
            .Select(incident => new OperationalIncidentDetailData(
                incident.Id,
                incident.OccurredAt,
                incident.Subsystem,
                incident.Severity,
                incident.Summary,
                incident.SafeDetail,
                incident.OrganizationId,
                incident.Organization != null ? incident.Organization.Name : null,
                incident.OrganizationMemberId,
                incident.RelatedResourceType,
                incident.RelatedResourceId,
                incident.CorrelationId,
                incident.Status,
                incident.ResolvedAt,
                incident.ResolvedByEmail,
                incident.ResolutionNotes))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static IQueryable<Domain.Entities.OperationalIncident> ApplyFilters(
        IQueryable<Domain.Entities.OperationalIncident> incidents,
        Guid? organizationId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        OperationalIncidentSubsystem? subsystem,
        OperationalIncidentSeverity? severity,
        string? correlationId)
    {
        if (organizationId is not null)
        {
            incidents = incidents.Where(incident => incident.OrganizationId == organizationId);
        }

        if (from is not null)
        {
            incidents = incidents.Where(incident => incident.OccurredAt >= from);
        }

        if (to is not null)
        {
            incidents = incidents.Where(incident => incident.OccurredAt <= to);
        }

        if (subsystem is not null)
        {
            incidents = incidents.Where(incident => incident.Subsystem == subsystem);
        }

        if (severity is not null)
        {
            incidents = incidents.Where(incident => incident.Severity == severity);
        }

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            incidents = incidents.Where(incident => incident.CorrelationId == correlationId);
        }

        return incidents;
    }
}
