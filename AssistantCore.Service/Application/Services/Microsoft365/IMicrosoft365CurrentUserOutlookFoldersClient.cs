namespace AssistantCore.Service.Application.Services.Microsoft365;

public interface IMicrosoft365CurrentUserOutlookFoldersClient
{
    Task<IReadOnlyCollection<Microsoft365CurrentUserOutlookFolder>> GetIndexableFoldersAsync(
        string tenantId,
        string entraUserId,
        CancellationToken cancellationToken = default);
}

public sealed record Microsoft365CurrentUserOutlookFolder(
    string FolderId,
    string DisplayName,
    string DisplayPath);
