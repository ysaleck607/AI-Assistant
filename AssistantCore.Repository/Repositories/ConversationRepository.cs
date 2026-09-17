using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using AssistantCore.Repository.Repositories.Audit;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Repository.Repositories;

public sealed class ConversationRepository(
    AssistantCoreDbContext dbContext,
    IAdministrativeAuditRepository administrativeAuditRepository)
    : IConversationRepository
{
    private const int InitialConversationVersion = 1;
    private const int MaximumAgentHistoryMessages = 20;

    public async Task<(Conversation Conversation, Message UserMessage)> CreateConversationWithFirstMessageAsync(
        Guid organizationId,
        Guid ownerMemberId,
        Conversation conversation,
        Message userMessage,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(
            conversation.OrganizationId,
            organizationId,
            nameof(conversation.OrganizationId));
        ValidateIdentifier(
            conversation.OwnerMemberId,
            ownerMemberId,
            nameof(conversation.OwnerMemberId));
        ValidateIdentifier(
            userMessage.ConversationId,
            conversation.Id,
            nameof(userMessage.ConversationId));

        conversation.OrganizationId = organizationId;
        conversation.OwnerMemberId = ownerMemberId;
        userMessage.ConversationId = conversation.Id;
        userMessage.Role = MessageRole.User;
        userMessage.ProcessingStatus = MessageProcessingStatus.Pending;
        conversation.Version = InitialConversationVersion;
        conversation.DeletedAt = null;

        conversation.Messages.Add(userMessage);
        dbContext.Conversations.Add(conversation);
        await dbContext.SaveChangesAsync(cancellationToken);

        return (conversation, userMessage);
    }

    public Task<Conversation?> FindConversationAsync(
        Guid organizationId,
        Guid ownerMemberId,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Conversations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                conversation =>
                    conversation.Id == conversationId
                    && conversation.OrganizationId == organizationId
                    && conversation.OwnerMemberId == ownerMemberId
                    && conversation.DeletedAt == null,
                cancellationToken);
    }

    public async Task<ConversationMessagePage> ListMessagesAsync(
        Guid conversationId,
        int limit,
        DateTimeOffset? cursorCreatedAt,
        Guid? cursorId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Messages
            .AsNoTracking()
            .Where(message => message.ConversationId == conversationId);

        if (cursorCreatedAt is not null && cursorId is not null)
        {
            query = query.Where(message =>
                message.CreatedAt < cursorCreatedAt
                || (message.CreatedAt == cursorCreatedAt && message.Id < cursorId));
        }

        var mostRecentFirst = await query
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .Take(limit + 1)
            .Select(message => new ConversationMessageItem(
                message.Id,
                message.Role,
                message.Content,
                message.ProcessingStatus,
                message.Model,
                message.CreatedAt,
                message.UpdatedAt,
                message.Sources
                    .Select(source => new ConversationMessageSourceItem(
                        source.SourceType,
                        source.Title,
                        source.Url,
                        source.Reference,
                        source.SourceDate))
                    .ToList()))
            .ToListAsync(cancellationToken);

        var hasMore = mostRecentFirst.Count > limit;
        var page = hasMore ? mostRecentFirst.Take(limit).ToList() : mostRecentFirst;
        var oldest = hasMore ? page[^1] : null;

        page.Reverse();

        return new ConversationMessagePage(
            page,
            hasMore,
            oldest?.CreatedAt,
            oldest?.Id);
    }

    public async Task<IReadOnlyList<ConversationMessageItem>> GetConversationHistoryAsync(
        Guid organizationId,
        Guid ownerMemberId,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var messages = await dbContext.Messages
            .AsNoTracking()
            .Where(message =>
                message.ConversationId == conversationId
                && message.Conversation.OrganizationId == organizationId
                && message.Conversation.OwnerMemberId == ownerMemberId
                && message.Conversation.DeletedAt == null
                && message.ProcessingStatus == MessageProcessingStatus.Completed
                && !message.Sources.Any(source => source.SourceType == "Microsoft365"))
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .Take(MaximumAgentHistoryMessages)
            .Select(message => new ConversationMessageItem(
                message.Id,
                message.Role,
                message.Content,
                message.ProcessingStatus,
                message.Model,
                message.CreatedAt,
                message.UpdatedAt,
                Array.Empty<ConversationMessageSourceItem>()))
            .ToListAsync(cancellationToken);

        messages.Reverse();
        return messages;
    }

    public async Task<(Conversation Conversation, IReadOnlyList<ConversationMessageItem> History)?>
        StartExistingConversationMessageAsync(
            Guid organizationId,
            Guid ownerMemberId,
            Guid conversationId,
            Message userMessage,
            CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(
            userMessage.ConversationId,
            conversationId,
            nameof(userMessage.ConversationId));

        var conversation = await dbContext.Conversations
            .Include(candidate => candidate.Messages
                .Where(message =>
                    message.ProcessingStatus == MessageProcessingStatus.Completed
                    && !message.Sources.Any(source => source.SourceType == "Microsoft365"))
                .OrderByDescending(message => message.CreatedAt)
                .ThenByDescending(message => message.Id)
                .Take(MaximumAgentHistoryMessages))
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == conversationId
                    && candidate.OrganizationId == organizationId
                    && candidate.OwnerMemberId == ownerMemberId
                    && candidate.DeletedAt == null,
                cancellationToken);

        if (conversation is null)
        {
            return null;
        }

        if (conversation.Status == ConversationStatus.Archived)
        {
            return (conversation, Array.Empty<ConversationMessageItem>());
        }

        var history = conversation.Messages
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Select(message => new ConversationMessageItem(
                message.Id,
                message.Role,
                message.Content,
                message.ProcessingStatus,
                message.Model,
                message.CreatedAt,
                message.UpdatedAt,
                Array.Empty<ConversationMessageSourceItem>()))
            .ToArray();

        userMessage.ConversationId = conversationId;
        userMessage.Role = MessageRole.User;
        userMessage.ProcessingStatus = MessageProcessingStatus.InProgress;
        conversation.UpdatedAt = userMessage.CreatedAt;
        dbContext.Messages.Add(userMessage);
        await dbContext.SaveChangesAsync(cancellationToken);

        return (conversation, history);
    }

    public async Task<ConversationListPage> ListConversationsAsync(
        Guid organizationId,
        Guid ownerMemberId,
        ConversationStatus status,
        int limit,
        DateTimeOffset? cursorUpdatedAt,
        Guid? cursorId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Conversations
            .AsNoTracking()
            .Where(conversation =>
                conversation.OrganizationId == organizationId
                && conversation.OwnerMemberId == ownerMemberId
                && conversation.Status == status
                && conversation.DeletedAt == null);

        if (cursorUpdatedAt is not null && cursorId is not null)
        {
            query = query.Where(conversation =>
                conversation.UpdatedAt < cursorUpdatedAt
                || (conversation.UpdatedAt == cursorUpdatedAt && conversation.Id < cursorId));
        }

        var items = await query
            .OrderByDescending(conversation => conversation.UpdatedAt)
            .ThenByDescending(conversation => conversation.Id)
            .Take(limit + 1)
            .Select(conversation => new ConversationListItem(
                conversation.Id,
                conversation.Title,
                conversation.Status,
                conversation.Version,
                conversation.CreatedAt,
                conversation.UpdatedAt,
                conversation.Messages
                    .OrderByDescending(message => message.CreatedAt)
                    .ThenByDescending(message => message.Id)
                    .Select(message => message.Content)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > limit;

        return new ConversationListPage(
            hasMore ? items.Take(limit).ToList() : items,
            hasMore);
    }

    public async Task<Message?> AddUserMessageAsync(
        Guid organizationId,
        Guid ownerMemberId,
        Guid conversationId,
        Message userMessage,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(
            userMessage.ConversationId,
            conversationId,
            nameof(userMessage.ConversationId));

        var conversation = await dbContext.Conversations
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == conversationId
                    && candidate.OrganizationId == organizationId
                    && candidate.OwnerMemberId == ownerMemberId
                    && candidate.DeletedAt == null,
                cancellationToken);

        if (conversation is null)
        {
            return null;
        }

        userMessage.ConversationId = conversationId;
        userMessage.Role = MessageRole.User;
        userMessage.ProcessingStatus = MessageProcessingStatus.Pending;
        conversation.UpdatedAt = userMessage.CreatedAt;

        dbContext.Messages.Add(userMessage);
        await dbContext.SaveChangesAsync(cancellationToken);

        return userMessage;
    }

    public async Task<bool> UpdateMessageProcessingStatusAsync(
        Guid organizationId,
        Guid ownerMemberId,
        Guid conversationId,
        Guid messageId,
        MessageProcessingStatus status,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var message = await dbContext.Messages
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == messageId
                    && candidate.Role == MessageRole.User
                    && candidate.ConversationId == conversationId
                    && candidate.Conversation.OrganizationId == organizationId
                    && candidate.Conversation.OwnerMemberId == ownerMemberId
                    && candidate.Conversation.DeletedAt == null,
                cancellationToken);

        if (message is null)
        {
            return false;
        }

        message.ProcessingStatus = status;
        message.UpdatedAt = updatedAt;
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<Message?> CompleteMessageWithAssistantResponseAsync(
        Guid organizationId,
        Guid ownerMemberId,
        Guid conversationId,
        Guid userMessageId,
        Message assistantMessage,
        IReadOnlyCollection<MessageSource> sources,
        IReadOnlyCollection<MessageWarning> warnings,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        var userMessage = await dbContext.Messages
            .Include(message => message.Conversation)
            .SingleOrDefaultAsync(
                message =>
                    message.Id == userMessageId
                    && message.Role == MessageRole.User
                    && message.ProcessingStatus == MessageProcessingStatus.InProgress
                    && message.ConversationId == conversationId
                    && message.Conversation.OrganizationId == organizationId
                    && message.Conversation.OwnerMemberId == ownerMemberId
                    && message.Conversation.DeletedAt == null,
                cancellationToken);

        if (userMessage is null)
        {
            return null;
        }

        assistantMessage.ConversationId = conversationId;
        assistantMessage.Role = MessageRole.Assistant;
        assistantMessage.ProcessingStatus = MessageProcessingStatus.Completed;

        foreach (var source in sources)
        {
            source.MessageId = assistantMessage.Id;
            assistantMessage.Sources.Add(source);
        }

        foreach (var warning in warnings)
        {
            warning.MessageId = assistantMessage.Id;
            assistantMessage.Warnings.Add(warning);
        }

        userMessage.ProcessingStatus = MessageProcessingStatus.Completed;
        userMessage.UpdatedAt = completedAt;
        userMessage.Conversation.UpdatedAt = completedAt;
        dbContext.Messages.Add(assistantMessage);

        await dbContext.SaveChangesAsync(cancellationToken);

        return assistantMessage;
    }

    public async Task<bool> FailMessageProcessingAsync(
        Guid organizationId,
        Guid ownerMemberId,
        Guid conversationId,
        Guid userMessageId,
        MessageProcessingStatus failureStatus,
        string errorCode,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken = default)
    {
        if (failureStatus is not MessageProcessingStatus.Failed
            and not MessageProcessingStatus.Cancelled)
        {
            throw new ArgumentOutOfRangeException(
                nameof(failureStatus),
                failureStatus,
                "The failure status must be Failed or Cancelled.");
        }

        var userMessage = await dbContext.Messages
            .SingleOrDefaultAsync(
                message =>
                    message.Id == userMessageId
                    && message.Role == MessageRole.User
                    && message.ProcessingStatus == MessageProcessingStatus.InProgress
                    && message.ConversationId == conversationId
                    && message.Conversation.OrganizationId == organizationId
                    && message.Conversation.OwnerMemberId == ownerMemberId
                    && message.Conversation.DeletedAt == null,
                cancellationToken);

        if (userMessage is null)
        {
            return false;
        }

        userMessage.ProcessingStatus = failureStatus;
        userMessage.ProcessingErrorCode = errorCode;
        userMessage.UpdatedAt = failedAt;

        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<ConversationUpdateResult> UpdateConversationAsync(
        Guid organizationId,
        Guid ownerMemberId,
        Guid conversationId,
        int? expectedVersion,
        string? title,
        ConversationStatus? status,
        DateTimeOffset updatedAt,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await dbContext.Conversations
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == conversationId
                    && candidate.OrganizationId == organizationId
                    && candidate.OwnerMemberId == ownerMemberId
                    && candidate.DeletedAt == null,
                cancellationToken);

        if (conversation is null)
        {
            return ConversationUpdateResult.NotFound;
        }

        if (expectedVersion is not null && conversation.Version != expectedVersion)
        {
            return ConversationUpdateResult.VersionConflict;
        }

        if (title is not null)
        {
            conversation.Title = title;
        }

        if (status is not null)
        {
            var previousStatus = conversation.Status;
            conversation.Status = status.Value;

            if (status.Value == ConversationStatus.Archived)
            {
                administrativeAuditRepository.Stage(AdministrativeAuditEntryFactory.Create(
                    organizationId,
                    AdministrativeAuditSubjectTypes.Member,
                    ownerMemberId,
                    AdministrativeAuditAction.ConversationArchived,
                    AdministrativeAuditSubjectTypes.Conversation,
                    conversationId,
                    updatedAt,
                    new Dictionary<string, object?> { ["status"] = previousStatus.ToString() },
                    new Dictionary<string, object?> { ["status"] = status.Value.ToString() },
                    correlationId));
            }
        }

        conversation.Version += 1;
        conversation.UpdatedAt = updatedAt;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConversationUpdateResult.VersionConflict;
        }

        return ConversationUpdateResult.Updated(conversation);
    }

    public async Task<ConversationDeleteStatus> SoftDeleteConversationAsync(
        Guid organizationId,
        Guid ownerMemberId,
        Guid conversationId,
        DateTimeOffset deletedAt,
        DateTimeOffset purgeAfter,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await dbContext.Conversations
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == conversationId
                    && candidate.OrganizationId == organizationId
                    && candidate.OwnerMemberId == ownerMemberId,
                cancellationToken);

        if (conversation is null)
        {
            return ConversationDeleteStatus.NotFound;
        }

        var alreadyRequested = await dbContext.ConversationPurgeRequests
            .AnyAsync(
                request => request.ConversationId == conversationId,
                cancellationToken);

        if (conversation.DeletedAt is not null && alreadyRequested)
        {
            return ConversationDeleteStatus.AlreadyDeleted;
        }

        if (conversation.DeletedAt is null)
        {
            conversation.DeletedAt = deletedAt;
            conversation.Version += 1;
            conversation.UpdatedAt = deletedAt;

            administrativeAuditRepository.Stage(AdministrativeAuditEntryFactory.Create(
                organizationId,
                AdministrativeAuditSubjectTypes.Member,
                ownerMemberId,
                AdministrativeAuditAction.ConversationDeleted,
                AdministrativeAuditSubjectTypes.Conversation,
                conversationId,
                deletedAt,
                new Dictionary<string, object?> { ["deletedAt"] = null },
                new Dictionary<string, object?> { ["deletedAt"] = deletedAt.ToString("O") },
                correlationId));
        }

        if (!alreadyRequested)
        {
            dbContext.ConversationPurgeRequests.Add(new ConversationPurgeRequest
            {
                Id = Guid.NewGuid(),
                ConversationId = conversationId,
                OrganizationId = organizationId,
                RequestedAt = deletedAt,
                PurgeAfter = purgeAfter,
                Status = ConversationPurgeStatus.Pending
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return ConversationDeleteStatus.Deleted;
    }

    public Task<int> ReencryptAllConversationTitlesAsync(
        int batchSize,
        CancellationToken cancellationToken = default) =>
        ReencryptAllAsync(
            skip => dbContext.Conversations
                .OrderBy(conversation => conversation.Id)
                .Skip(skip)
                .Take(batchSize)
                .ToListAsync(cancellationToken),
            conversation => dbContext.Entry(conversation).Property(c => c.Title).IsModified = true,
            cancellationToken);

    public Task<int> ReencryptAllMessageContentAsync(
        int batchSize,
        CancellationToken cancellationToken = default) =>
        ReencryptAllAsync(
            skip => dbContext.Messages
                .OrderBy(message => message.Id)
                .Skip(skip)
                .Take(batchSize)
                .ToListAsync(cancellationToken),
            message => dbContext.Entry(message).Property(m => m.Content).IsModified = true,
            cancellationToken);

    public Task<int> ReencryptAllMessageWarningContentAsync(
        int batchSize,
        CancellationToken cancellationToken = default) =>
        ReencryptAllAsync(
            skip => dbContext.MessageWarnings
                .OrderBy(warning => warning.Id)
                .Skip(skip)
                .Take(batchSize)
                .ToListAsync(cancellationToken),
            warning => dbContext.Entry(warning).Property(w => w.Content).IsModified = true,
            cancellationToken);

    private async Task<int> ReencryptAllAsync<TEntity>(
        Func<int, Task<List<TEntity>>> fetchBatch,
        Action<TEntity> markModified,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var totalProcessed = 0;
        while (true)
        {
            var batch = await fetchBatch(totalProcessed);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var entity in batch)
            {
                markModified(entity);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            totalProcessed += batch.Count;
        }

        return totalProcessed;
    }

    private static void ValidateIdentifier(
        Guid currentValue,
        Guid expectedValue,
        string parameterName)
    {
        if (currentValue != Guid.Empty && currentValue != expectedValue)
        {
            throw new ArgumentException(
                $"{parameterName} does not match the authenticated context.",
                parameterName);
        }
    }
}
