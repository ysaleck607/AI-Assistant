using AssistantCore.Repository.Domain.Entities;

namespace AssistantCore.Repository.Repositories;

public interface IMicrosoft365DriveRepository
{
    Task<Microsoft365Drive?> FindAsync(
        Guid organizationId,
        string driveId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bibliotheques SharePoint activees pour l'indexation dans l'organisation. Les OneDrive
    /// individuels et les listes sont exclus : ils ne sont pas repris par la reindexation.
    /// </summary>
    Task<IReadOnlyCollection<Microsoft365Drive>> GetIndexedSharePointDrivesAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<Microsoft365Drive>>([]);

    Task<IReadOnlyCollection<Microsoft365Drive>> GetByOwnerAsync(
        Guid organizationId,
        string ownerUserObjectId,
        CancellationToken cancellationToken = default);

    Task<Microsoft365Drive> SaveOneDriveAsync(
        Microsoft365Connection connection,
        string driveId,
        string ownerUserObjectId,
        string? ownerUserPrincipalName,
        string displayName,
        string? webUrl,
        DateTimeOffset discoveredAt,
        CancellationToken cancellationToken = default);
}
