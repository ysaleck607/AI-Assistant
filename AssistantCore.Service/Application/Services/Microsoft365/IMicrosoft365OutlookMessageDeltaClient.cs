using AssistantCore.Service.Application.Models.Microsoft365;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public interface IMicrosoft365OutlookMessageDeltaClient
{
    IAsyncEnumerable<Microsoft365OutlookMessageDeltaPage> GetInitialPagesAsync(
        string tenantId,
        string mailboxUserId,
        string mailFolderId,
        DateTimeOffset receivedSince,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<Microsoft365OutlookMessageDeltaPage> GetDeltaPagesAsync(
        string tenantId,
        string deltaLink,
        CancellationToken cancellationToken = default);
}
