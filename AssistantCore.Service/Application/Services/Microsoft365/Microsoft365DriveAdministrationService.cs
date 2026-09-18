using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Abstractions;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Services.AuthenticateUser;
using AssistantCore.Service.Application.Services.Messages.Tools;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public sealed class Microsoft365DriveAdministrationService(
    IAuthenticateUserService authenticateUserService,
    IMicrosoft365ConnectionRepository connectionRepository,
    IMicrosoft365SourceDiscoveryRepository sourceRepository,
    IMicrosoft365SiteClient siteClient,
    IMicrosoft365SiteSourcesDiscoveryService discoveryService,
    IMicrosoft365IndexedContentRepository indexedContentRepository,
    IMicrosoft365PassageIndexWriter indexWriter,
    TimeProvider timeProvider,
    IAiToolRegistry? toolRegistry = null) : IMicrosoft365DriveAdministrationService
{
    public async Task<Microsoft365SiteResponse> RegisterSiteAsync(
        string siteId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);
        var (organization, member) = await authenticateUserService.GetOrganizationAsync(cancellationToken);
        EnsureAdmin(member.Role);
        var connection = await connectionRepository.FindActiveByOrganizationAsync(
            organization.Id,
            cancellationToken) ?? throw new NotFoundException("Active Microsoft 365 connection was not found.");
        var external = await siteClient.GetAsync(connection.TenantId!, siteId, cancellationToken);
        var site = await sourceRepository.SaveSiteAsync(
            connection,
            external.SiteId,
            external.DisplayName,
            external.WebUrl,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return new Microsoft365SiteResponse(
            site.SiteId,
            site.DisplayName,
            site.WebUrl,
            site.Status.ToString());
    }

    public async Task<IReadOnlyCollection<Microsoft365DriveResponse>> GetDrivesAsync(
        string siteId,
        CancellationToken cancellationToken = default)
    {
        var (organization, member) = await authenticateUserService.GetOrganizationAsync(cancellationToken);
        EnsureAdmin(member.Role);
        await discoveryService.DiscoverAsync(siteId, cancellationToken);
        var drives = await sourceRepository.GetDrivesAsync(
            organization.Id,
            siteId,
            cancellationToken);
        return drives.Select(MapDrive).ToArray();
    }

    public async Task<Microsoft365DriveResponse> EnableDriveAsync(
        string siteId,
        string driveId,
        bool isIndexed,
        CancellationToken cancellationToken = default)
    {
        var (organization, member) = await authenticateUserService.GetOrganizationAsync(cancellationToken);
        EnsureAdmin(member.Role);
        var drive = await sourceRepository.FindDriveAsync(
            organization.Id,
            siteId,
            driveId,
            cancellationToken) ?? throw new NotFoundException("Microsoft 365 drive was not found.");
        if (!isIndexed)
        {
            await sourceRepository.SaveDriveDeactivationAsync(
                drive,
                timeProvider.GetUtcNow(),
                cancellationToken);
            toolRegistry?.InvalidateCache(organization.Id);
            var contents = await indexedContentRepository.GetBySourceAsync(
                organization.Id,
                drive.Id,
                cancellationToken);
            var chunkIds = contents.SelectMany(content => content.Passages)
                .Select(passage => passage.ChunkId)
                .ToArray();
            if (chunkIds.Length > 0)
            {
                await indexWriter.DeleteAsync(organization.Id, chunkIds, cancellationToken);
            }

            foreach (var content in contents)
            {
                await indexedContentRepository.DeleteAsync(content, cancellationToken);
            }

            return MapDrive(drive);
        }

        await sourceRepository.SaveDriveActivationAsync(
            drive,
            timeProvider.GetUtcNow(),
            cancellationToken);
        toolRegistry?.InvalidateCache(organization.Id);
        return MapDrive(drive);
    }

    private static void EnsureAdmin(OrganizationRole role)
    {
        if (role != OrganizationRole.Admin)
        {
            throw new ForbiddenException("Administrator access required.");
        }
    }

    private static Microsoft365DriveResponse MapDrive(AssistantCore.Repository.Domain.Entities.Microsoft365Drive drive) =>
        new(
            drive.SiteId,
            drive.DriveId,
            drive.DisplayName,
            drive.WebUrl,
            drive.Status.ToString(),
            drive.IsIndexed,
            drive.Kind switch
            {
                Microsoft365SourceKind.SharePointDrive => "sharepoint",
                Microsoft365SourceKind.OneDrive => "onedrive",
                _ => throw new InvalidOperationException("Unsupported Microsoft 365 drive source type.")
            },
            drive.OwnerUserObjectId,
            drive.OwnerUserPrincipalName);
}
