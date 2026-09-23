using AssistantCore.ExternalServices.Entities.Microsoft;
using AssistantCore.ExternalServices.Services.Microsoft;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Infrastructure.Microsoft365;

public sealed class Microsoft365CurrentUserOutlookFoldersClientAdapter(
    MicrosoftGraphMailFolderClient graphClient,
    IMicrosoft365ApplicationTokenClient applicationTokenClient,
    IOptions<Microsoft365Options> options) : IMicrosoft365CurrentUserOutlookFoldersClient
{
    private static readonly string[] ExcludedWellKnownFolderIds =
        ["deleteditems", "junkemail"];
    private static readonly string[] ExcludedRootDisplayNames =
        ["Deleted Items", "Junk Email"];

    public async Task<IReadOnlyCollection<Microsoft365CurrentUserOutlookFolder>> GetIndexableFoldersAsync(
        string tenantId,
        string entraUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entraUserId);

        var accessToken = await applicationTokenClient.AcquireGraphTokenAsync(
            tenantId,
            cancellationToken);
        var rootFolders = await graphClient.GetRootFoldersAsync(
            options.Value.GraphBaseUrl,
            accessToken,
            entraUserId,
            cancellationToken);
        var excludedFolderIds = await GetExcludedFolderIdsAsync(
            accessToken,
            entraUserId,
            cancellationToken);

        var folders = new List<Microsoft365CurrentUserOutlookFolder>();
        foreach (var folder in rootFolders.OrderBy(folder => folder.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            if (IsExcludedRootFolder(folder, excludedFolderIds))
            {
                continue;
            }

            await AddFolderTreeAsync(
                accessToken,
                entraUserId,
                folder,
                folder.DisplayName,
                folders,
                cancellationToken);
        }

        return folders;
    }

    private async Task<HashSet<string>> GetExcludedFolderIdsAsync(
        string accessToken,
        string entraUserId,
        CancellationToken cancellationToken)
    {
        var folderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var wellKnownFolderId in ExcludedWellKnownFolderIds)
        {
            var folder = await graphClient.GetFolderAsync(
                options.Value.GraphBaseUrl,
                accessToken,
                entraUserId,
                wellKnownFolderId,
                cancellationToken);
            if (folder is not null)
            {
                folderIds.Add(folder.Id);
            }
        }

        return folderIds;
    }

    private async Task AddFolderTreeAsync(
        string accessToken,
        string entraUserId,
        MicrosoftMailFolder folder,
        string displayPath,
        List<Microsoft365CurrentUserOutlookFolder> folders,
        CancellationToken cancellationToken)
    {
        folders.Add(new Microsoft365CurrentUserOutlookFolder(
            folder.Id,
            folder.DisplayName,
            displayPath));

        if (folder.ChildFolderCount == 0)
        {
            return;
        }

        var childFolders = await graphClient.GetChildFoldersAsync(
            options.Value.GraphBaseUrl,
            accessToken,
            entraUserId,
            folder.Id,
            cancellationToken);
        foreach (var childFolder in childFolders.OrderBy(child => child.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            await AddFolderTreeAsync(
                accessToken,
                entraUserId,
                childFolder,
                $"{displayPath}/{childFolder.DisplayName}",
                folders,
                cancellationToken);
        }
    }

    private static bool IsExcludedRootFolder(
        MicrosoftMailFolder folder,
        HashSet<string> excludedFolderIds) =>
        excludedFolderIds.Contains(folder.Id)
        ||
        ExcludedRootDisplayNames.Any(name =>
            string.Equals(name, folder.DisplayName, StringComparison.OrdinalIgnoreCase));
}
