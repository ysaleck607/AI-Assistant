using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Repository.Queries;

public sealed class BackofficeAuditQueries(AssistantCoreDbContext dbContext) : IBackofficeAuditQueries
{
    public async Task<BackofficeAuditListPageData> SearchAsync(
        Guid? organizationId,
        Guid? actorId,
        string? action,
        string? result,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // Aucune ecriture d'echec n'existe aujourd'hui (voir les appelants de
        // AdministrativeAuditEntryFactory.Create) : toute entree est implicitement
        // "Succeeded". Un filtre explicite sur "Failed" ne peut donc jamais matcher.
        if (string.Equals(result, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            return new BackofficeAuditListPageData([], page, pageSize, 0);
        }

        var entries = ApplyFilters(
            dbContext.AdministrativeAuditEntries.AsNoTracking(),
            organizationId,
            actorId,
            action,
            from,
            to);

        var totalCount = await entries.CountAsync(cancellationToken);
        var items = await entries
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenBy(entry => entry.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(entry => new BackofficeAuditEntryData(
                entry.Id,
                entry.OccurredAt,
                entry.Action,
                entry.ActorId,
                entry.OrganizationId,
                entry.Organization.Name,
                entry.TargetType,
                entry.TargetId,
                entry.CorrelationId,
                entry.OldValues,
                entry.NewValues))
            .ToListAsync(cancellationToken);

        return new BackofficeAuditListPageData(items, page, pageSize, totalCount);
    }

    private static IQueryable<AdministrativeAuditEntry> ApplyFilters(
        IQueryable<AdministrativeAuditEntry> entries,
        Guid? organizationId,
        Guid? actorId,
        string? action,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        if (organizationId is not null)
        {
            entries = entries.Where(entry => entry.OrganizationId == organizationId);
        }

        if (actorId is not null)
        {
            entries = entries.Where(entry => entry.ActorId == actorId);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            // Un filtre fourni mais non reconnu ne peut correspondre a aucune entree
            // (l'action est un enum ferme) plutot que d'etre ignore silencieusement.
            entries = Enum.TryParse<AdministrativeAuditAction>(action, ignoreCase: true, out var parsedAction)
                ? entries.Where(entry => entry.Action == parsedAction)
                : entries.Where(_ => false);
        }

        if (from is not null)
        {
            entries = entries.Where(entry => entry.OccurredAt >= from);
        }

        if (to is not null)
        {
            entries = entries.Where(entry => entry.OccurredAt <= to);
        }

        return entries;
    }
}
