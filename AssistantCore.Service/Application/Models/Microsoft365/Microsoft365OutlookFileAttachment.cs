namespace AssistantCore.Service.Application.Models.Microsoft365;

public sealed record Microsoft365OutlookFileAttachment(
    string Id,
    string Name,
    string? ContentType,
    byte[] Content,
    bool IsInline);
