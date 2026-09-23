using AssistantCore.Repository.Queries;
using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Services.Backoffice;

public sealed class BackofficeUsageService(
    IBackofficeUsageQueries usageQueries,
    TimeProvider timeProvider) : IBackofficeUsageService
{
    private const int DefaultDailySeriesDays = 30;
    private const int MaximumDailySeriesDays = 90;

    public async Task<BackofficeUsageSummaryDto> GetSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        var data = await usageQueries.GetSummaryAsync(timeProvider.GetUtcNow(), cancellationToken);
        return BackofficeUsageSummaryDto.FromData(data);
    }

    public async Task<BackofficeUsageDailySeriesResponse> GetDailySeriesAsync(
        int days,
        CancellationToken cancellationToken = default)
    {
        var normalizedDays = days <= 0
            ? DefaultDailySeriesDays
            : Math.Min(days, MaximumDailySeriesDays);

        var data = await usageQueries.GetDailySeriesAsync(
            timeProvider.GetUtcNow(),
            normalizedDays,
            cancellationToken);

        return BackofficeUsageDailySeriesResponse.FromData(data);
    }

    public async Task<BackofficeUsageByOrganizationResponse> GetByOrganizationAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var periodStart = from ?? new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var periodEndExclusive = to ?? periodStart.AddMonths(1);

        var data = await usageQueries.GetByOrganizationAsync(
            periodStart,
            periodEndExclusive,
            cancellationToken);

        return BackofficeUsageByOrganizationResponse.FromData(data);
    }
}
