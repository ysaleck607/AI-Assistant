namespace AssistantCore.Service.Application.Models.Microsoft365;

public sealed record Microsoft365ConsentState(
    Guid OrganizationId,
    Guid Nonce,
    DateTimeOffset ExpiresAt,
    string? EntraUserId = null);
