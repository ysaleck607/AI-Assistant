using AssistantCore.Repository.Domain.Entities;

namespace AssistantCore.Repository.Repositories;

public interface IMicrosoft365SourceDiscoveryRepository
{
    Task<IReadOnlyCollection<string>> GetSiteIdsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Microsoft365SharePointSiteData>> GetIndexedSitesAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<Microsoft365SharePointSiteData>>([]);

    Task<bool> HasIndexedSourceAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) => Task.FromResult(false);

    Task<bool> IsEnvironmentReadyAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) => Task.FromResult(false);

    Task<Microsoft365Site?> FindSiteAsync(
        Guid organizationId,
        string siteId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Microsoft365List>> GetListsAsync(
        Guid organizationId,
        string siteId,
        CancellationToken cancellationToken = default);

    Task<Microsoft365Site> SaveSiteAsync(
        Microsoft365Connection connection,
        string siteId,
        string displayName,
        string webUrl,
        DateTimeOffset discoveredAt,
        CancellationToken cancellationToken = default) =>
        Task.FromException<Microsoft365Site>(new NotSupportedException());

    Task<IReadOnlyCollection<Microsoft365Drive>> GetDrivesAsync(
        Guid organizationId,
        string siteId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<Microsoft365Drive>>([]);

    Task<Microsoft365Drive?> FindDriveAsync(
        Guid organizationId,
        string siteId,
        string driveId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<Microsoft365Drive?>(null);

    Task SaveDriveActivationAsync(
        Microsoft365Drive drive,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException());

    Task SaveDriveDeactivationAsync(
        Microsoft365Drive drive,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException());

    Task<Microsoft365Source> SaveOutlookMailboxAsync(
        Microsoft365Connection connection,
        string mailboxUserId,
        string mailFolderId,
        string displayName,
        DateTimeOffset discoveredAt,
        CancellationToken cancellationToken = default) =>
        Task.FromException<Microsoft365Source>(new NotSupportedException());

    Task SaveOutlookMailboxActivationAsync(
        Microsoft365Source mailbox,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException());

    Task<Microsoft365List?> FindListAsync(
        Guid organizationId,
        string siteId,
        string listId,
        CancellationToken cancellationToken = default);

    Task<Microsoft365ListIndexingRequestCounts> SaveListActivationAsync(
        Microsoft365List list,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken = default);

    Task<Microsoft365ListIndexingRequestCounts> SaveListDeactivationAsync(
        Microsoft365List list,
        DateTimeOffset requestedAt,
        bool requestIndexCleanup,
        CancellationToken cancellationToken = default);

    Task ReconcileSiteSourcesAsync(
        Microsoft365Site site,
        IReadOnlyCollection<Microsoft365SourceDiscoveryData> drives,
        IReadOnlyCollection<Microsoft365SourceDiscoveryData> lists,
        DateTimeOffset discoveredAt,
        CancellationToken cancellationToken = default);
}
