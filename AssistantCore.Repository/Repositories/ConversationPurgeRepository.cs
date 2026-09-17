using System.Data;
using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Repository.Repositories;

public sealed class ConversationPurgeRepository(AssistantCoreDbContext dbContext)
    : IConversationPurgeRepository
{
    public async Task<ConversationPurgeRequest?> ClaimNextAsync(
        Guid leaseId,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken = default)
    {
        // Serializable : deux workers qui reclament en meme temps ne doivent pas
        // obtenir la meme demande, sans quoi la purge serait executee deux fois.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var request = await dbContext.ConversationPurgeRequests
            .Where(candidate =>
                candidate.PurgeAfter <= now
                && (candidate.Status == ConversationPurgeStatus.Pending
                    || candidate.Status == ConversationPurgeStatus.TemporaryFailure
                        && (candidate.NextAttemptAt == null || candidate.NextAttemptAt <= now)
                    // Un bail expire signale un worker disparu en cours de route.
                    || candidate.Status == ConversationPurgeStatus.Processing
                        && candidate.LeaseExpiresAt <= now))
            .OrderBy(candidate => candidate.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (request is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        request.Status = ConversationPurgeStatus.Processing;
        request.LeaseId = leaseId;
        request.LeaseExpiresAt = leaseExpiresAt;
        request.AttemptCount++;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return request;
    }

    public async Task<int> DeleteMessageSourcesAsync(
        Guid purgeRequestId,
        Guid leaseId,
        CancellationToken cancellationToken = default)
    {
        var request = await FindLeasedAsync(purgeRequestId, leaseId, cancellationToken);
        if (request is null) return 0;

        var sources = await dbContext.MessageSources
            .Where(source => source.Message.ConversationId == request.ConversationId)
            .ToListAsync(cancellationToken);
        var warnings = await dbContext.MessageWarnings
            .Where(warning => warning.Message.ConversationId == request.ConversationId)
            .ToListAsync(cancellationToken);

        dbContext.MessageSources.RemoveRange(sources);
        dbContext.MessageWarnings.RemoveRange(warnings);
        await dbContext.SaveChangesAsync(cancellationToken);

        return sources.Count;
    }

    public async Task<int> DeleteMessagesAsync(
        Guid purgeRequestId,
        Guid leaseId,
        CancellationToken cancellationToken = default)
    {
        var request = await FindLeasedAsync(purgeRequestId, leaseId, cancellationToken);
        if (request is null) return 0;

        var messages = await dbContext.Messages
            .Where(message => message.ConversationId == request.ConversationId)
            .ToListAsync(cancellationToken);
        dbContext.Messages.RemoveRange(messages);
        await dbContext.SaveChangesAsync(cancellationToken);

        return messages.Count;
    }

    public async Task DeleteConversationAsync(
        Guid purgeRequestId,
        Guid leaseId,
        CancellationToken cancellationToken = default)
    {
        var request = await FindLeasedAsync(purgeRequestId, leaseId, cancellationToken);
        if (request is null) return;

        var conversation = await dbContext.Conversations
            .FirstOrDefaultAsync(
                candidate => candidate.Id == request.ConversationId,
                cancellationToken);
        if (conversation is null) return;

        dbContext.Conversations.Remove(conversation);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> VerifyPurgedAsync(
        Guid purgeRequestId,
        CancellationToken cancellationToken = default)
    {
        var request = await dbContext.ConversationPurgeRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == purgeRequestId, cancellationToken);
        if (request is null) return false;

        var conversationRemains = await dbContext.Conversations
            .AsNoTracking()
            .AnyAsync(candidate => candidate.Id == request.ConversationId, cancellationToken);
        var messagesRemain = await dbContext.Messages
            .AsNoTracking()
            .AnyAsync(message => message.ConversationId == request.ConversationId, cancellationToken);
        var sourcesRemain = await dbContext.MessageSources
            .AsNoTracking()
            .AnyAsync(
                source => source.Message.ConversationId == request.ConversationId,
                cancellationToken);

        return !conversationRemains && !messagesRemain && !sourcesRemain;
    }

    public async Task AdvanceAsync(
        Guid purgeRequestId,
        Guid leaseId,
        ConversationPurgeStep nextStep,
        CancellationToken cancellationToken = default)
    {
        var request = await FindLeasedAsync(purgeRequestId, leaseId, cancellationToken);
        if (request is null) return;

        request.Step = nextStep;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(
        Guid purgeRequestId,
        Guid leaseId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        var request = await FindLeasedAsync(purgeRequestId, leaseId, cancellationToken);
        if (request is null) return;

        request.Status = ConversationPurgeStatus.Completed;
        request.CompletedAt = completedAt;
        request.LeaseId = null;
        request.LeaseExpiresAt = null;
        request.LastErrorCode = null;
        request.NextAttemptAt = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(
        Guid purgeRequestId,
        Guid leaseId,
        string errorCode,
        bool isPermanent,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken = default)
    {
        var request = await FindLeasedAsync(purgeRequestId, leaseId, cancellationToken);
        if (request is null) return;

        request.Status = isPermanent
            ? ConversationPurgeStatus.PermanentFailure
            : ConversationPurgeStatus.TemporaryFailure;
        request.LastErrorCode = errorCode;
        request.NextAttemptAt = isPermanent ? null : nextAttemptAt;
        request.LeaseId = null;
        request.LeaseExpiresAt = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Une ecriture n'est permise qu'au detenteur du bail. Un worker dont le bail
    /// a expire, et dont la demande a ete reprise ailleurs, ne doit plus rien
    /// modifier : il obtient null et s'arrete sans consequence.
    /// </summary>
    private Task<ConversationPurgeRequest?> FindLeasedAsync(
        Guid purgeRequestId,
        Guid leaseId,
        CancellationToken cancellationToken) =>
        dbContext.ConversationPurgeRequests
            .FirstOrDefaultAsync(
                candidate => candidate.Id == purgeRequestId && candidate.LeaseId == leaseId,
                cancellationToken);
}
