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
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var warnings = ApplyFilters(
            dbContext.MessageWarnings.AsNoTracking(),
            organizationId,
            from,
            to);

        var totalCount = await warnings.CountAsync(cancellationToken);
        var items = await warnings
            .OrderByDescending(warning => warning.Message.CreatedAt)
            .ThenBy(warning => warning.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(warning => new BackofficeMessageWarningSummaryData(
                warning.Id,
                warning.MessageId,
                warning.Message.ConversationId,
                warning.Message.Conversation.OrganizationId,
                warning.Message.Conversation.Organization.Name,
                warning.Content,
                warning.Message.CreatedAt))
            .ToListAsync(cancellationToken);

        return new BackofficeMessageWarningListPageData(items, page, pageSize, totalCount);
    }

    private static IQueryable<Domain.Entities.MessageWarning> ApplyFilters(
        IQueryable<Domain.Entities.MessageWarning> warnings,
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
}
