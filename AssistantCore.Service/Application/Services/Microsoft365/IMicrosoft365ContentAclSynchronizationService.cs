using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Models.Microsoft365.Permissions;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public interface IMicrosoft365ContentAclSynchronizationService
{
    /// <summary>
    /// Registers passages that were already uploaded as unavailable. This operation only persists
    /// their synchronization state; publishing remains a separate operation.
    /// </summary>
    Task RegisterAsync(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        IReadOnlyCollection<string> chunkIds,
        string aclFingerprint,
        string? siteUrl,
        CancellationToken cancellationToken = default);

    Task<bool> MarkUnavailableIfRegisteredAsync(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        CancellationToken cancellationToken = default);

    Task<Microsoft365AclSynchronizationResult> SynchronizeIfRegisteredAsync(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        Microsoft365Acl acl,
        CancellationToken cancellationToken = default);

    Task<Microsoft365AclSynchronizationResult> SynchronizeAsync(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        Microsoft365Acl acl,
        CancellationToken cancellationToken = default);
}
