using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Repository.Queries;

public sealed class BackofficeOrganizationQueries(
    AssistantCoreDbContext dbContext,
    IEmailBlindIndexHasher emailHasher)
    : IBackofficeOrganizationQueries
{
    public async Task<BackofficeOrganizationListPageData> SearchOrganizationsAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var organizations = ApplySearch(
            dbContext.Organizations.AsNoTracking(),
            search);

        var totalCount = await organizations.CountAsync(cancellationToken);
        var items = await organizations
            .OrderBy(organization => organization.Name)
            .ThenBy(organization => organization.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(organization => new BackofficeOrganizationListItemData(
                organization.Id,
                organization.Name,
                organization.ExternalTenantId
                    ?? dbContext.Microsoft365Connections
                        .Where(connection => connection.OrganizationId == organization.Id)
                        .Select(connection => connection.TenantId)
                        .FirstOrDefault(),
                organization.Status,
                organization.Members.Count(),
                organization.Connectors.Count(connector =>
                    connector.Status == RecordStatus.Active
                    && connector.IsConfigured),
                dbContext.Microsoft365IndexedContents.Count(content =>
                    content.OrganizationId == organization.Id
                    && content.IsAvailable),
                dbContext.Microsoft365Sources
                    .Where(source => source.Microsoft365Connection.OrganizationId == organization.Id)
                    .OrderByDescending(source => source.LastSuccessfulSynchronizationAt)
                    .Select(source => source.LastSuccessfulSynchronizationAt)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new BackofficeOrganizationListPageData(items, page, pageSize, totalCount);
    }

    public async Task<BackofficeOrganizationDetailsData?> GetOrganizationDetailsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var organization = await dbContext.Organizations
            .AsNoTracking()
            .Where(candidate => candidate.Id == organizationId)
            .Select(candidate => new BackofficeOrganizationData(
                candidate.Id,
                candidate.Name,
                candidate.ExternalTenantId
                    ?? dbContext.Microsoft365Connections
                        .Where(connection => connection.OrganizationId == candidate.Id)
                        .Select(connection => connection.TenantId)
                        .FirstOrDefault(),
                candidate.Status,
                candidate.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);

        if (organization is null)
        {
            return null;
        }

        var users = await dbContext.OrganizationMembers
            .AsNoTracking()
            .Where(member => member.OrganizationId == organizationId)
            .GroupBy(member => member.OrganizationId)
            .Select(group => new BackofficeOrganizationUsersData(
                group.Count(),
                group.Count(member => member.Status == RecordStatus.Active)))
            .SingleOrDefaultAsync(cancellationToken)
            ?? new BackofficeOrganizationUsersData(0, 0);

        var microsoft = await dbContext.Microsoft365Connections
            .AsNoTracking()
            .Where(connection => connection.OrganizationId == organizationId)
            .Select(connection => new BackofficeOrganizationMicrosoftData(
                connection.Status == Microsoft365ConnectionStatus.Active,
                connection.ConsentValidatedAt != null
                    && connection.Status == Microsoft365ConnectionStatus.Active))
            .SingleOrDefaultAsync(cancellationToken)
            ?? new BackofficeOrganizationMicrosoftData(false, false);

        var sources = new BackofficeOrganizationSourcesData(
            await dbContext.Microsoft365Sites
                .AsNoTracking()
                .CountAsync(site =>
                    site.OrganizationId == organizationId
                    && (dbContext.Microsoft365Drives.Any(drive =>
                            drive.OrganizationId == organizationId
                            && drive.SiteId == site.SiteId
                            && drive.Kind == Microsoft365SourceKind.SharePointDrive
                            && drive.IsIndexed
                            && (drive.Status == Microsoft365SourceStatus.Enabled
                                || drive.Status == Microsoft365SourceStatus.FullResyncRequired))
                        || dbContext.Microsoft365Lists.Any(list =>
                            list.OrganizationId == organizationId
                            && list.SiteId == site.SiteId
                            && list.IsIndexed
                            && (list.Status == Microsoft365SourceStatus.Enabled
                                || list.Status == Microsoft365SourceStatus.FullResyncRequired))),
                    cancellationToken),
            await dbContext.Microsoft365Drives
                .AsNoTracking()
                .CountAsync(drive =>
                    drive.OrganizationId == organizationId
                    && drive.Kind == Microsoft365SourceKind.OneDrive
                    && drive.IsIndexed,
                    cancellationToken));

        var indexing = await CreateIndexingDataAsync(
            organizationId,
            microsoft.Connected,
            cancellationToken);

        return new BackofficeOrganizationDetailsData(
            organization,
            users,
            microsoft,
            sources,
            indexing);
    }

    private IQueryable<Domain.Entities.Organization> ApplySearch(
        IQueryable<Domain.Entities.Organization> organizations,
        string? search)
    {
        var normalizedSearch = search?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedSearch))
        {
            return organizations;
        }

        var hasOrganizationId = Guid.TryParse(normalizedSearch, out var organizationId);

        // OrganizationMember.Email is encrypted at rest: EF Core cannot translate a
        // Contains() over it (it tries to encrypt the search term and use the resulting
        // ciphertext as a LIKE escape character, which SQL Server rejects). The blind-index
        // hash only supports exact matches, so a search that happens to be a full email
        // address now matches that admin's organization; a partial email no longer does -
        // an accepted narrowing versus the previous 500 on every search.
        var searchEmailHash = emailHasher.ComputeHash(normalizedSearch);

        return organizations.Where(organization =>
            organization.Name.Contains(normalizedSearch)
            || (organization.ExternalTenantId != null
                && organization.ExternalTenantId.Contains(normalizedSearch))
            || dbContext.Microsoft365Connections.Any(connection =>
                connection.OrganizationId == organization.Id
                && connection.TenantId != null
                && connection.TenantId.Contains(normalizedSearch))
            || organization.Members.Any(member =>
                member.Role == OrganizationRole.Admin
                && member.EmailLookupHash == searchEmailHash)
            || (hasOrganizationId && organization.Id == organizationId));
    }

    private async Task<BackofficeOrganizationIndexingData> CreateIndexingDataAsync(
        Guid organizationId,
        bool isMicrosoftConnected,
        CancellationToken cancellationToken)
    {
        var documentCount = await dbContext.Microsoft365IndexedContents
            .AsNoTracking()
            .CountAsync(content =>
                content.OrganizationId == organizationId
                && content.IsAvailable,
                cancellationToken);

        var lastSyncAt = await dbContext.Microsoft365Sources
            .AsNoTracking()
            .Where(source => source.Microsoft365Connection.OrganizationId == organizationId)
            .OrderByDescending(source => source.LastSuccessfulSynchronizationAt)
            .Select(source => source.LastSuccessfulSynchronizationAt)
            .FirstOrDefaultAsync(cancellationToken);

        var hasIndexingError = await dbContext.Microsoft365Sources
            .AsNoTracking()
            .AnyAsync(source =>
                source.Microsoft365Connection.OrganizationId == organizationId
                && (source.Status == Microsoft365SourceStatus.Error
                    || source.Status == Microsoft365SourceStatus.Unavailable),
                cancellationToken);

        return new BackofficeOrganizationIndexingData(
            documentCount,
            lastSyncAt,
            ResolveIndexingStatus(isMicrosoftConnected, hasIndexingError));
    }

    private static string ResolveIndexingStatus(
        bool isMicrosoftConnected,
        bool hasIndexingError)
    {
        if (hasIndexingError)
        {
            return "Error";
        }

        return isMicrosoftConnected ? "Healthy" : "NotConfigured";
    }
}
