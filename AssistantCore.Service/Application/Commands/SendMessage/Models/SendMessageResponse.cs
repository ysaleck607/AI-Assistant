namespace AssistantCore.Service.Application.Commands.SendMessage.Models;

public sealed record SendMessageResponse(
    Guid ConversationId,
    Guid MessageId,
    string Answer,
    string Model,
    IReadOnlyCollection<MessageSourceResponse> Sources,
    IReadOnlyCollection<string> Warnings,
    DateTimeOffset CreatedAt);
