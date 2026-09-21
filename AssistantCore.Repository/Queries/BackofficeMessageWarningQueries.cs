using AssistantCore.Repository.Domain;
using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Repository.Queries;

public sealed class BackofficeMessageWarningQueries(AssistantCoreDbContext dbContext)
    : IBackofficeMessageWarningQueries
{
    public async Task<BackofficeMessageWarningListPageData> SearchAsync(
        Guid? organizationId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        bool contentGapsOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var warnings = ApplyFilters(
            dbContext.MessageWarnings.AsNoTracking(),
            organizationId,
            from,
            to);

        // Content is encrypted at rest (EncryptedStringConverter), so the content-gap
        // marker prefix cannot be matched in SQL. Every row in scope is materialized
        // (decrypting Content) and the marker/pagination are applied in memory.
        var candidates = await warnings
            .OrderByDescending(warning => warning.Message.CreatedAt)
            .ThenBy(warning => warning.Id)
            .Select(warning => new CandidateRow(
                warning.Id,
                warning.MessageId,
                warning.Message.ConversationId,
                warning.Message.Conversation.OrganizationId,
                warning.Message.Conversation.Organization.Name,
                warning.Content,
                warning.Message.CreatedAt))
            .ToListAsync(cancellationToken);

        var classified = candidates
            .Select(candidate => (
                Candidate: candidate,
                IsContentGap: candidate.Content.StartsWith(
                    MessageWarningMarkers.NoEvidenceFoundPrefix,
                    StringComparison.Ordinal)))
            .Where(item => !contentGapsOnly || item.IsContentGap)
            .ToArray();

        var totalCount = classified.Length;
        var pageItems = classified
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArray();

        var questionTextsByMessageId = await LoadQuestionTextsAsync(
            pageItems
                .Where(item => item.IsContentGap)
                .Select(item => item.Candidate)
                .ToArray(),
            cancellationToken);

        var items = pageItems
            .Select(item => new BackofficeMessageWarningSummaryData(
                item.Candidate.Id,
                item.Candidate.MessageId,
                item.Candidate.ConversationId,
                item.Candidate.OrganizationId,
                item.Candidate.OrganizationName,
                StripContentGapMarker(item.Candidate.Content, item.IsContentGap),
                item.Candidate.OccurredAt,
                item.IsContentGap,
                item.IsContentGap
                    ? questionTextsByMessageId.GetValueOrDefault(item.Candidate.MessageId)
                    : null))
            .ToList();

        return new BackofficeMessageWarningListPageData(items, page, pageSize, totalCount);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> LoadQuestionTextsAsync(
        IReadOnlyCollection<CandidateRow> contentGapCandidates,
        CancellationToken cancellationToken)
    {
        if (contentGapCandidates.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var conversationIds = contentGapCandidates
            .Select(candidate => candidate.ConversationId)
            .Distinct()
            .ToArray();

        // Message.Content is also encrypted: userMessages is materialized (decrypted)
        // before matching the closest preceding user question per candidate.
        var userMessages = await dbContext.Messages
            .AsNoTracking()
            .Where(message =>
                conversationIds.Contains(message.ConversationId)
                && message.Role == MessageRole.User)
            .Select(message => new
            {
                message.ConversationId,
                message.CreatedAt,
                message.Content
            })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, string>();
        foreach (var candidate in contentGapCandidates)
        {
            var question = userMessages
                .Where(message =>
                    message.ConversationId == candidate.ConversationId
                    && message.CreatedAt <= candidate.OccurredAt)
                .OrderByDescending(message => message.CreatedAt)
                .FirstOrDefault();

            if (question is not null)
            {
                result[candidate.MessageId] = question.Content;
            }
        }

        return result;
    }

    private static string StripContentGapMarker(string content, bool isContentGap) =>
        isContentGap
            ? content[MessageWarningMarkers.NoEvidenceFoundPrefix.Length..].TrimStart()
            : content;

    private static IQueryable<MessageWarning> ApplyFilters(
        IQueryable<MessageWarning> warnings,
        Guid? organizationId,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        if (organizationId is not null)
        {
            warnings = warnings.Where(
                warning => warning.Message.Conversation.OrganizationId == organizationId);
        }

        if (from is not null)
        {
            warnings = warnings.Where(warning => warning.Message.CreatedAt >= from);
        }

        if (to is not null)
        {
            warnings = warnings.Where(warning => warning.Message.CreatedAt <= to);
        }

        return warnings;
    }

    private sealed record CandidateRow(
        Guid Id,
        Guid MessageId,
        Guid ConversationId,
        Guid OrganizationId,
        string OrganizationName,
        string Content,
        DateTimeOffset OccurredAt);
}
