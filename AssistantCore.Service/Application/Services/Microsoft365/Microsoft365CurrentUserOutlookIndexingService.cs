using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Services.Messages.Tools;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public sealed class Microsoft365CurrentUserOutlookIndexingService(
    IMicrosoft365ConnectionRepository connectionRepository,
    IMicrosoft365SourceDiscoveryRepository sourceRepository,
    IMicrosoft365CurrentUserOutlookFoldersClient foldersClient,
    TimeProvider timeProvider,
    IAiToolRegistry? toolRegistry = null) : IMicrosoft365CurrentUserOutlookIndexingService
{
    public async Task EnsureIndexedAsync(
        Guid organizationId,
        string entraUserId,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty || string.IsNullOrWhiteSpace(entraUserId))
        {
            return;
        }

        var connection = await connectionRepository.FindActiveByOrganizationAsync(
            organizationId,
            cancellationToken);
        if (connection is null || string.IsNullOrWhiteSpace(connection.TenantId))
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var folders = await foldersClient.GetIndexableFoldersAsync(
            connection.TenantId,
            entraUserId,
            cancellationToken);
        foreach (var folder in folders)
        {
            var mailbox = await sourceRepository.SaveOutlookMailboxAsync(
                connection,
                entraUserId,
                folder.FolderId,
                $"Outlook - {folder.DisplayPath}",
                now,
                cancellationToken);

            await sourceRepository.SaveOutlookMailboxActivationAsync(
                mailbox,
                now,
                cancellationToken);
        }

        toolRegistry?.InvalidateCache(organizationId);
    }
}
