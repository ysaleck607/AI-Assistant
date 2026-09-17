using AssistantCore.Service.Application.Commands.SendMessage.Models;
using AssistantCore.Service.Application.Models.Messages;
using AssistantCore.Service.Application.Models.Messages.AgentRuntime;
using AssistantCore.Service.Application.Models.Messages.Lifecycle;

namespace AssistantCore.Service.Application.Services.Messages.Responses;

public sealed class SendMessageResponseFactory : ISendMessageResponseFactory
{
    public SendMessageResponse Create(
        StartedMessageProcessing processing,
        AgentTurnResult agentTurnResult,
        CompletedMessageProcessing completedProcessing) =>
        new(
            processing.ConversationId,
            completedProcessing.AssistantMessageId,
            agentTurnResult.Content,
            agentTurnResult.ModelName,
            agentTurnResult.Citations.Select(MapSource).ToArray(),
            agentTurnResult.Warnings.Where(IsUserFacingWarning).ToArray(),
            completedProcessing.CreatedAt);

    private static MessageSourceResponse MapSource(RetrievedEvidence evidence) =>
        new(
            evidence.SourceType,
            evidence.Title,
            evidence.Url,
            evidence.Reference);

    private static bool IsUserFacingWarning(string warning) =>
        !string.IsNullOrWhiteSpace(warning)
        && !warning.StartsWith("rag.", StringComparison.OrdinalIgnoreCase);
}
