using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Services.Conversations;
using AssistantCore.Service.Application.Services.Conversations.Pagination;

namespace AssistantCore.Service.Tests.Conversations;

public sealed class ConversationMessageListingServiceTests
{
    [Theory, AutoDomainData]
    public async Task Given_AnUnownedConversation_When_ListAsync_Then_ReturnsNull(
        Guid organizationId,
        Guid ownerMemberId,
        Guid conversationId)
    {
        // Given
        var repository = new RecordingConversationRepository(foundConversation: null);
        var service = new ConversationMessageListingService(repository, new ConversationMessageCursorCodec());

        // When
        var result = await service.ListAsync(
            organizationId, ownerMemberId, conversationId, 50, null, null, CancellationToken.None);

        // Then
        Assert.Null(result);
        Assert.False(repository.ListMessagesWasCalled);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnOwnedConversation_When_ListAsync_Then_MapsMessagesAndSources(
        Guid organizationId,
        Guid ownerMemberId,
        Guid conversationId,
        Conversation conversation,
        DateTimeOffset createdAt)
    {
        // Given
        var source = new ConversationMessageSourceItem(
            "SharePoint", "Titre", "https://example.com", "ref-1", createdAt);
        var messageItem = new ConversationMessageItem(
            Guid.NewGuid(),
            MessageRole.Assistant,
            "Reponse",
            MessageProcessingStatus.Completed,
            "gpt",
            createdAt,
            createdAt,
            [source]);
        var page = new ConversationMessagePage([messageItem], HasMore: false, null, null);
        var repository = new RecordingConversationRepository(conversation, page);
        var service = new ConversationMessageListingService(repository, new ConversationMessageCursorCodec());

        // When
        var result = await service.ListAsync(
            organizationId, ownerMemberId, conversationId, 50, null, null, CancellationToken.None);

        // Then
        Assert.NotNull(result);
        var returnedMessage = Assert.Single(result.Items);
        Assert.Equal(messageItem.Id, returnedMessage.Id);
        Assert.Equal("Assistant", returnedMessage.Role);
        Assert.Equal("Completed", returnedMessage.ProcessingStatus);
        Assert.Equal("gpt", returnedMessage.Model);
        var returnedSource = Assert.Single(returnedMessage.Sources);
        Assert.Equal("SharePoint", returnedSource.Type);
        Assert.Equal("ref-1", returnedSource.Reference);
        Assert.False(result.HasMore);
        Assert.Null(result.NextCursor);
    }

    [Theory, AutoDomainData]
    public async Task Given_APageWithMore_When_ListAsync_Then_EncodesTheNextCursorForTheConversation(
        Guid organizationId,
        Guid ownerMemberId,
        Guid conversationId,
        Conversation conversation,
        DateTimeOffset oldestCreatedAt,
        Guid oldestId)
    {
        // Given
        var page = new ConversationMessagePage([], HasMore: true, oldestCreatedAt, oldestId);
        var repository = new RecordingConversationRepository(conversation, page);
        var codec = new ConversationMessageCursorCodec();
        var service = new ConversationMessageListingService(repository, codec);

        // When
        var result = await service.ListAsync(
            organizationId, ownerMemberId, conversationId, 50, null, null, CancellationToken.None);

        // Then
        Assert.NotNull(result);
        Assert.NotNull(result.NextCursor);
        var decoded = codec.Decode(result.NextCursor, conversationId);
        Assert.Equal(oldestCreatedAt, decoded!.CreatedAt);
        Assert.Equal(oldestId, decoded.Id);
    }

    private sealed class RecordingConversationRepository(
        Conversation? foundConversation,
        ConversationMessagePage? page = null) : IConversationRepository
    {
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

        public bool ListMessagesWasCalled { get; private set; }

        public Task<Conversation?> FindConversationAsync(
            Guid organizationId,
            Guid ownerMemberId,
            Guid conversationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(foundConversation);

        public Task<ConversationMessagePage> ListMessagesAsync(
            Guid conversationId,
            int limit,
            DateTimeOffset? cursorCreatedAt,
            Guid? cursorId,
            CancellationToken cancellationToken = default)
        {
            ListMessagesWasCalled = true;
            return Task.FromResult(page ?? new ConversationMessagePage([], false, null, null));
        }

        public Task<IReadOnlyList<ConversationMessageItem>> GetConversationHistoryAsync(
            Guid organizationId,
            Guid ownerMemberId,
            Guid conversationId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<(Conversation Conversation, Message UserMessage)> CreateConversationWithFirstMessageAsync(
            Guid organizationId,
            Guid ownerMemberId,
            Conversation conversation,
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

        public Task<int> ReencryptAllConversationTitlesAsync(
            int batchSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> ReencryptAllMessageContentAsync(
            int batchSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> ReencryptAllMessageWarningContentAsync(
            int batchSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
