namespace AssistantCore.ExternalServices.Entities.Microsoft;

public sealed record MicrosoftUserProfile(
    string Id,
    string UserType,
    string UserPrincipalName);
