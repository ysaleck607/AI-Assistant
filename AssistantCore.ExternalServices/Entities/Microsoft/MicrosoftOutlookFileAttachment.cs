namespace AssistantCore.ExternalServices.Entities.Microsoft;

public sealed record MicrosoftOutlookFileAttachment(
    string Id,
    string Name,
    string? ContentType,
    byte[] Content,
    bool IsInline);
