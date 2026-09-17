using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Services.Conversations;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssistantCore.Service.Tests.Conversations;

public sealed class ConversationEncryptionBackfillServiceTests
{
    [Theory, AutoDomainData]
    public async Task Given_APositiveBatchSize_When_RunAsync_Then_ReturnsCountsFromEachRepositoryCall(
        int conversationCount,
        int messageCount,
        int warningCount)
    {
        // Given
        var repository = new RecordingConversationRepository(conversationCount, messageCount, warningCount);
        var service = new ConversationEncryptionBackfillService(
            repository,
            NullLogger<ConversationEncryptionBackfillService>.Instance);

        // When
        var summary = await service.RunAsync(100, CancellationToken.None);

        // Then
        Assert.Equal(conversationCount, summary.ConversationTitlesProcessed);
        Assert.Equal(messageCount, summary.MessageContentsProcessed);
        Assert.Equal(warningCount, summary.MessageWarningContentsProcessed);
        Assert.Equal(100, repository.ReceivedBatchSize);
    }

    [Fact]
    public async Task Given_AZeroBatchSize_When_RunAsync_Then_ThrowsArgumentOutOfRangeException()
    {
        // Given
        var repository = new RecordingConversationRepository(0, 0, 0);
        var service = new ConversationEncryptionBackfillService(
            repository,
            NullLogger<ConversationEncryptionBackfillService>.Instance);

        // When / Then
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.RunAsync(0, CancellationToken.None));
    }

    private sealed class RecordingConversationRepository(
        int conversationCount,
        int messageCount,
        int warningCount) : IConversationRepository
    {
        public int? ReceivedBatchSize { get; private set; }

        public Task<int> ReencryptAllConversationTitlesAsync(
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            ReceivedBatchSize = batchSize;
            return Task.FromResult(conversationCount);
        }

        public Task<int> ReencryptAllMessageContentAsync(
            int batchSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(messageCount);

        public Task<int> ReencryptAllMessageWarningContentAsync(
            int batchSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(warningCount);

        public Task<(Conversation Conversation, Message UserMessage)> CreateConversationWithFirstMessageAsync(
            Guid organizationId,
            Guid ownerMemberId,
            Conversation conversation,
            Message userMessage,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Conversation?> FindConversationAsync(
            Guid organizationId,
            Guid ownerMemberId,
            Guid conversationId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConversationMessagePage> ListMessagesAsync(
            Guid conversationId,
            int limit,
            DateTimeOffset? cursorCreatedAt,
            Guid? cursorId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ConversationMessageItem>> GetConversationHistoryAsync(
            Guid organizationId,
            Guid ownerMemberId,
            Guid conversationId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<(Conversation Conversation, IReadOnlyList<ConversationMessageItem> History)?>
            StartExistingConversationMessageAsync(
                Guid organizationId,
                Guid ownerMemberId,
                Guid conversationId,
                Message userMessage,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConversationListPage> ListConversationsAsync(
            Guid organizationId,
            Guid ownerMemberId,
            ConversationStatus status,
            int limit,
            DateTimeOffset? cursorUpdatedAt,
            Guid? cursorId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Message?> AddUserMessageAsync(
            Guid organizationId,
            Guid ownerMemberId,
            Guid conversationId,
            Message userMessage,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> UpdateMessageProcessingStatusAsync(
            Guid organizationId,
            Guid ownerMemberId,
            Guid conversationId,
            Guid messageId,
            MessageProcessingStatus status,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Message?> CompleteMessageWithAssistantResponseAsync(
            Guid organizationId,
            Guid ownerMemberId,
            Guid conversationId,
            Guid userMessageId,
            Message assistantMessage,
            IReadOnlyCollection<MessageSource> sources,
            IReadOnlyCollection<MessageWarning> warnings,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConversationUpdateResult> UpdateConversationAsync(
            Guid organizationId,
            Guid ownerMemberId,
            Guid conversationId,
            int? expectedVersion,
            string? title,
            ConversationStatus? status,
            DateTimeOffset updatedAt,
            string correlationId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConversationDeleteStatus> SoftDeleteConversationAsync(
            Guid organizationId,
            Guid ownerMemberId,
            Guid conversationId,
            DateTimeOffset deletedAt,
            DateTimeOffset purgeAfter,
            string correlationId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> FailMessageProcessingAsync(
            Guid organizationId,
            Guid ownerMemberId,
            Guid conversationId,
            Guid userMessageId,
            MessageProcessingStatus failureStatus,
            string errorCode,
            DateTimeOffset failedAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
