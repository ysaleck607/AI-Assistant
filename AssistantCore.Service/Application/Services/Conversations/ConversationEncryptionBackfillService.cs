using AssistantCore.Repository.Repositories;
using Microsoft.Extensions.Logging;

namespace AssistantCore.Service.Application.Services.Conversations;

public sealed class ConversationEncryptionBackfillService(
    IConversationRepository conversationRepository,
    ILogger<ConversationEncryptionBackfillService> logger) : IConversationEncryptionBackfillService
{
    public async Task<ConversationEncryptionBackfillSummary> RunAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(batchSize, 0);

        var conversationTitlesProcessed = await conversationRepository.ReencryptAllConversationTitlesAsync(
            batchSize,
            cancellationToken);
        logger.LogInformation(
            "Conversation encryption backfill re-encrypted {Count} conversation title(s).",
            conversationTitlesProcessed);

        var messageContentsProcessed = await conversationRepository.ReencryptAllMessageContentAsync(
            batchSize,
            cancellationToken);
        logger.LogInformation(
            "Conversation encryption backfill re-encrypted {Count} message content(s).",
            messageContentsProcessed);

        var messageWarningContentsProcessed = await conversationRepository.ReencryptAllMessageWarningContentAsync(
            batchSize,
            cancellationToken);
        logger.LogInformation(
            "Conversation encryption backfill re-encrypted {Count} message warning content(s).",
            messageWarningContentsProcessed);

        return new ConversationEncryptionBackfillSummary(
            conversationTitlesProcessed,
            messageContentsProcessed,
            messageWarningContentsProcessed);
    }
}
