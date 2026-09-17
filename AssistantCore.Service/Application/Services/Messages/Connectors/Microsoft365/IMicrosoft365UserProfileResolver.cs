namespace AssistantCore.Service.Application.Services.Messages.Connectors.Microsoft365;

public interface IMicrosoft365UserProfileResolver
{
    Task<Microsoft365UserProfile?> ResolveAsync(
        string externalTenantId,
        string entraUserId,
        CancellationToken cancellationToken);
}

public sealed record Microsoft365UserProfile(
    bool IsGuest,
    string UserPrincipalName);
