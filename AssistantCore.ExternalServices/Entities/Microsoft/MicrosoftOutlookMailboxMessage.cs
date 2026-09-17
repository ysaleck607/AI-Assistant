namespace AssistantCore.ExternalServices.Entities.Microsoft;

public sealed record MicrosoftOutlookMailboxMessage(
    string Id,
    string Subject,
    string BodyPreview,
    string? BodyContent,
    string? SenderName,
    string? SenderAddress,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset? SentAt,
    string? WebLink);
