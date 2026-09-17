namespace AssistantCore.Service.Application.Services.Conversations;

public interface IConversationEncryptionBackfillService
{
    Task<ConversationEncryptionBackfillSummary> RunAsync(
        int batchSize,
        CancellationToken cancellationToken = default);
}

public sealed record ConversationEncryptionBackfillSummary(
    int ConversationTitlesProcessed,
    int MessageContentsProcessed,
    int MessageWarningContentsProcessed);
