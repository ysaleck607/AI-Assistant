using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;

namespace AssistantCore.Repository.Repositories;

public interface IConversationRepository
{
    Task<(Conversation Conversation, Message UserMessage)> CreateConversationWithFirstMessageAsync(
        Guid organizationId,
        Guid ownerMemberId,
        Conversation conversation,
        Message userMessage,
        CancellationToken cancellationToken = default);

    Task<Conversation?> FindConversationAsync(
        Guid organizationId,
        Guid ownerMemberId,
        Guid conversationId,
        CancellationToken cancellationToken = default);

    Task<ConversationMessagePage> ListMessagesAsync(
        Guid conversationId,
        int limit,
        DateTimeOffset? cursorCreatedAt,
        Guid? cursorId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConversationMessageItem>> GetConversationHistoryAsync(
        Guid organizationId,
        Guid ownerMemberId,
        Guid conversationId,
        CancellationToken cancellationToken = default);

    async Task<(Conversation Conversation, IReadOnlyList<ConversationMessageItem> History)?>
        StartExistingConversationMessageAsync(
            Guid organizationId,
            Guid ownerMemberId,
            Guid conversationId,
            Message userMessage,
            CancellationToken cancellationToken = default)
    {
        var conversation = await FindConversationAsync(
            organizationId,
            ownerMemberId,
            conversationId,
            cancellationToken);
        if (conversation is null || conversation.Status == ConversationStatus.Archived)
        {
            return conversation is null
                ? null
                : (conversation, Array.Empty<ConversationMessageItem>());
        }

        var history = await GetConversationHistoryAsync(
            organizationId,
            ownerMemberId,
            conversationId,
            cancellationToken);
        var addedMessage = await AddUserMessageAsync(
            organizationId,
            ownerMemberId,
            conversationId,
            userMessage,
            cancellationToken);
        if (addedMessage is null)
        {
            return null;
        }

        var updated = await UpdateMessageProcessingStatusAsync(
            organizationId,
            ownerMemberId,
            conversationId,
            userMessage.Id,
            MessageProcessingStatus.InProgress,
            userMessage.UpdatedAt,
            cancellationToken);

        return updated ? (conversation, history) : null;
    }

    /// <summary>
    /// Retourne une page de resumes de conversations visibles portant le statut
    /// demande, triees par UpdatedAt puis Id decroissants. Une conversation
    /// supprimee logiquement n'est jamais retournee, quel que soit ce statut.
    /// Lit toujours limit + 1 elements pour determiner s'il existe une page
    /// suivante, sans jamais charger l'historique complet des messages.
    /// </summary>
    Task<ConversationListPage> ListConversationsAsync(
        Guid organizationId,
        Guid ownerMemberId,
        ConversationStatus status,
        int limit,
        DateTimeOffset? cursorUpdatedAt,
        Guid? cursorId,
        CancellationToken cancellationToken = default);

    Task<Message?> AddUserMessageAsync(
        Guid organizationId,
        Guid ownerMemberId,
        Guid conversationId,
        Message userMessage,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateMessageProcessingStatusAsync(
        Guid organizationId,
        Guid ownerMemberId,
        Guid conversationId,
        Guid messageId,
        MessageProcessingStatus status,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task<Message?> CompleteMessageWithAssistantResponseAsync(
        Guid organizationId,
        Guid ownerMemberId,
        Guid conversationId,
        Guid userMessageId,
        Message assistantMessage,
        IReadOnlyCollection<MessageSource> sources,
        IReadOnlyCollection<MessageWarning> warnings,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);

    Task<ConversationUpdateResult> UpdateConversationAsync(
        Guid organizationId,
        Guid ownerMemberId,
        Guid conversationId,
        int? expectedVersion,
        string? title,
        ConversationStatus? status,
        DateTimeOffset updatedAt,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<ConversationDeleteStatus> SoftDeleteConversationAsync(
        Guid organizationId,
        Guid ownerMemberId,
        Guid conversationId,
        DateTimeOffset deletedAt,
        DateTimeOffset purgeAfter,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<bool> FailMessageProcessingAsync(
        Guid organizationId,
        Guid ownerMemberId,
        Guid conversationId,
        Guid userMessageId,
        MessageProcessingStatus failureStatus,
        string errorCode,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken = default);

    Task<int> ReencryptAllConversationTitlesAsync(int batchSize, CancellationToken cancellationToken = default);

    Task<int> ReencryptAllMessageContentAsync(int batchSize, CancellationToken cancellationToken = default);

    Task<int> ReencryptAllMessageWarningContentAsync(int batchSize, CancellationToken cancellationToken = default);
}
