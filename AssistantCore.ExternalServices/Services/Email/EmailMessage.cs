namespace AssistantCore.ExternalServices.Services.Email;

public sealed record EmailMessage(
    string ToAddress,
    string Subject,
    string PlainTextBody);
