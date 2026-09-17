using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Repository.Repositories;

public sealed class Microsoft365DriveRepository(AssistantCoreDbContext dbContext)
    : IMicrosoft365DriveRepository
{
    public Task<Microsoft365Drive?> FindAsync(
        Guid organizationId,
        string driveId,
        CancellationToken cancellationToken = default) =>
        dbContext.Microsoft365Drives
            .Include(drive => drive.Microsoft365Connection)
            .Include(drive => drive.OrganizationConnector)
            .Include(drive => drive.Synchronizations)
            .Include(drive => drive.Subscriptions)
            .SingleOrDefaultAsync(drive =>
                drive.OrganizationId == organizationId
                && drive.DriveId == driveId,
                cancellationToken);

    public async Task<IReadOnlyCollection<Microsoft365Drive>> GetIndexedSharePointDrivesAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Microsoft365Drives
            .AsNoTracking()
            .Where(drive =>
                drive.OrganizationId == organizationId
                && drive.Kind == Microsoft365SourceKind.SharePointDrive
                && drive.IsIndexed
                && drive.Status == Microsoft365SourceStatus.Enabled)
            .OrderBy(drive => drive.DisplayName)
            .ThenBy(drive => drive.DriveId)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<Microsoft365Drive>> GetByOwnerAsync(
        Guid organizationId,
        string ownerUserObjectId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Microsoft365Drives
            .AsNoTracking()
            .Where(drive =>
                drive.OrganizationId == organizationId
                && drive.Kind == Microsoft365SourceKind.OneDrive
                && drive.OwnerUserObjectId == ownerUserObjectId)
            .OrderBy(drive => drive.DisplayName)
            .ThenBy(drive => drive.DriveId)
            .ToArrayAsync(cancellationToken);

    public async Task<Microsoft365Drive> SaveOneDriveAsync(
        Microsoft365Connection connection,
        string driveId,
        string ownerUserObjectId,
        string? ownerUserPrincipalName,
        string displayName,
        string? webUrl,
        DateTimeOffset discoveredAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(driveId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserObjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var drive = await dbContext.Microsoft365Drives.SingleOrDefaultAsync(candidate =>
            candidate.OrganizationId == connection.OrganizationId
            && candidate.DriveId == driveId,
            cancellationToken);

        if (drive is null)
        {
            drive = new Microsoft365Drive
            {
                Id = Guid.NewGuid(),
                Microsoft365ConnectionId = connection.Id,
                OrganizationId = connection.OrganizationId,
                OrganizationConnectorId = connection.OrganizationConnectorId,
                SiteId = null,
                DriveId = driveId,
                OwnerUserObjectId = ownerUserObjectId,
                OwnerUserPrincipalName = ownerUserPrincipalName,
                Kind = Microsoft365SourceKind.OneDrive,
                ExternalResourceId = driveId,
                ParentExternalResourceId = null,
                DisplayName = displayName,
                WebUrl = webUrl,
                Status = Microsoft365SourceStatus.Discovered,
                IsIndexed = false,
                DiscoveredAt = discoveredAt
            };
            dbContext.Microsoft365Drives.Add(drive);
        }
        else
        {
            if (drive.Kind != Microsoft365SourceKind.OneDrive)
            {
                throw new InvalidOperationException(
                    "The Microsoft 365 drive is already registered as a different source type.");
            }

            if (!string.Equals(drive.OwnerUserObjectId, ownerUserObjectId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The OneDrive is already registered for a different owner.");
            }

            drive.OwnerUserPrincipalName = ownerUserPrincipalName;
            drive.RefreshDiscovery(displayName, webUrl);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return drive;
    }
}
