using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Repository.Queries;

/// <summary>
/// Un "utilisateur actif" est un membre d'organisation (Conversation.OwnerMemberId) ayant
/// envoye au moins un message (Role=User, c'est-a-dire une vraie requete, jamais une reponse
/// assistant) dans la periode. Les jours sont delimites en UTC.
/// </summary>
public sealed class BackofficeUsageQueries(AssistantCoreDbContext dbContext) : IBackofficeUsageQueries
{
    public async Task<BackofficeUsageSummaryData> GetSummaryAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var todayStart = StartOfDay(now);
        var todayEnd = todayStart.AddDays(1);
        var weekStart = todayStart.AddDays(-6);
        var monthStart = todayStart.AddDays(-29);

        var userMessages = dbContext.Messages.AsNoTracking()
            .Where(message => message.Role == MessageRole.User);

        var requestsToday = await userMessages
            .CountAsync(message => message.CreatedAt >= todayStart && message.CreatedAt < todayEnd, cancellationToken);

        var activeTodayMemberIds = await userMessages
            .Where(message => message.CreatedAt >= todayStart && message.CreatedAt < todayEnd)
            .Select(message => message.Conversation.OwnerMemberId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var weeklyActiveUsers = await userMessages
            .Where(message => message.CreatedAt >= weekStart && message.CreatedAt < todayEnd)
            .Select(message => message.Conversation.OwnerMemberId)
            .Distinct()
            .CountAsync(cancellationToken);

        var monthlyActiveUsers = await userMessages
            .Where(message => message.CreatedAt >= monthStart && message.CreatedAt < todayEnd)
            .Select(message => message.Conversation.OwnerMemberId)
            .Distinct()
            .CountAsync(cancellationToken);

        // "Recurrent" : ce membre avait deja au moins un message avant aujourd'hui.
        // "Nouveau" : son tout premier message, tous temps confondus, tombe aujourd'hui.
        var returningTodayCount = activeTodayMemberIds.Count == 0
            ? 0
            : await userMessages
                .Where(message =>
                    message.CreatedAt < todayStart
                    && activeTodayMemberIds.Contains(message.Conversation.OwnerMemberId))
                .Select(message => message.Conversation.OwnerMemberId)
                .Distinct()
                .CountAsync(cancellationToken);

        var dailyActiveUsers = activeTodayMemberIds.Count;
        var newUsersToday = dailyActiveUsers - returningTodayCount;
        var requestsPerActiveUser = dailyActiveUsers == 0
            ? 0d
            : (double)requestsToday / dailyActiveUsers;

        return new BackofficeUsageSummaryData(
            dailyActiveUsers,
            requestsToday,
            requestsPerActiveUser,
            weeklyActiveUsers,
            monthlyActiveUsers,
            newUsersToday,
            returningTodayCount);
    }

    public async Task<IReadOnlyList<BackofficeUsageDailyPointData>> GetDailySeriesAsync(
        DateTimeOffset now,
        int days,
        CancellationToken cancellationToken = default)
    {
        var todayStart = StartOfDay(now);
        var periodStart = todayStart.AddDays(-(days - 1));
        var periodEndExclusive = todayStart.AddDays(1);

        // Materialise tout d'un coup : regrouper par jour + compter des utilisateurs
        // distincts par jour ne se traduit pas de facon fiable en SQL via EF, et le
        // volume (quelques dizaines de jours de messages) reste largement gerable en
        // memoire pour un outil d'administration.
        var rows = await dbContext.Messages.AsNoTracking()
            .Where(message =>
                message.Role == MessageRole.User
                && message.CreatedAt >= periodStart
                && message.CreatedAt < periodEndExclusive)
            .Select(message => new { message.CreatedAt, message.Conversation.OwnerMemberId })
            .ToListAsync(cancellationToken);

        var points = new List<BackofficeUsageDailyPointData>(days);
        for (var offset = 0; offset < days; offset++)
        {
            var dayStart = periodStart.AddDays(offset);
            var dayEnd = dayStart.AddDays(1);
            var dayRows = rows
                .Where(row => row.CreatedAt >= dayStart && row.CreatedAt < dayEnd)
                .ToArray();

            points.Add(new BackofficeUsageDailyPointData(
                DateOnly.FromDateTime(dayStart.UtcDateTime),
                dayRows.Select(row => row.OwnerMemberId).Distinct().Count(),
                dayRows.Length));
        }

        return points;
    }

    public async Task<IReadOnlyList<BackofficeUsageByOrganizationData>> GetByOrganizationAsync(
        DateTimeOffset periodStart,
        DateTimeOffset periodEndExclusive,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Messages.AsNoTracking()
            .Where(message =>
                message.Role == MessageRole.User
                && message.CreatedAt >= periodStart
                && message.CreatedAt < periodEndExclusive)
            .Select(message => new
            {
                message.Conversation.OrganizationId,
                message.Conversation.Organization.Name,
                message.Conversation.OwnerMemberId,
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => new { row.OrganizationId, row.Name })
            .Select(group => new BackofficeUsageByOrganizationData(
                group.Key.OrganizationId,
                group.Key.Name,
                group.Select(row => row.OwnerMemberId).Distinct().Count(),
                group.Count()))
            .OrderByDescending(data => data.Requests)
            .ToList();
    }

    private static DateTimeOffset StartOfDay(DateTimeOffset now) =>
        new(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
}
