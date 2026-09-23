using AssistantCore.Repository.Queries;
using AssistantCore.Service.Application.Services.Backoffice;

namespace AssistantCore.Service.Tests.Backoffice;

public sealed class BackofficeUsageServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-22T12:00:00Z");

    [Fact]
    public async Task Given_NoDaysSpecified_When_GetDailySeriesAsync_Then_DefaultsToThirtyDays()
    {
        // Given
        var queries = new RecordingUsageQueries();
        var service = new BackofficeUsageService(queries, new StubTimeProvider(Now));

        // When
        await service.GetDailySeriesAsync(days: 0);

        // Then
        Assert.Equal(30, queries.RequestedDays);
    }

    [Fact]
    public async Task Given_DaysAboveTheMaximum_When_GetDailySeriesAsync_Then_ClampsToNinety()
    {
        // Given
        var queries = new RecordingUsageQueries();
        var service = new BackofficeUsageService(queries, new StubTimeProvider(Now));

        // When
        await service.GetDailySeriesAsync(days: 365);

        // Then
        Assert.Equal(90, queries.RequestedDays);
    }

    [Fact]
    public async Task Given_NoPeriodSpecified_When_GetByOrganizationAsync_Then_DefaultsToTheCurrentCalendarMonth()
    {
        // Given
        var queries = new RecordingUsageQueries();
        var service = new BackofficeUsageService(queries, new StubTimeProvider(Now));

        // When
        await service.GetByOrganizationAsync(from: null, to: null);

        // Then
        Assert.Equal(DateTimeOffset.Parse("2026-09-01T00:00:00Z"), queries.RequestedPeriodStart);
        Assert.Equal(DateTimeOffset.Parse("2026-10-01T00:00:00Z"), queries.RequestedPeriodEndExclusive);
    }

    private sealed class StubTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingUsageQueries : IBackofficeUsageQueries
    {
        public int RequestedDays { get; private set; }

        public DateTimeOffset RequestedPeriodStart { get; private set; }

        public DateTimeOffset RequestedPeriodEndExclusive { get; private set; }

        public Task<BackofficeUsageSummaryData> GetSummaryAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BackofficeUsageSummaryData(0, 0, 0, 0, 0, 0, 0));

        public Task<IReadOnlyList<BackofficeUsageDailyPointData>> GetDailySeriesAsync(
            DateTimeOffset now,
            int days,
            CancellationToken cancellationToken = default)
        {
            RequestedDays = days;
            return Task.FromResult<IReadOnlyList<BackofficeUsageDailyPointData>>([]);
        }

        public Task<IReadOnlyList<BackofficeUsageByOrganizationData>> GetByOrganizationAsync(
            DateTimeOffset periodStart,
            DateTimeOffset periodEndExclusive,
            CancellationToken cancellationToken = default)
        {
            RequestedPeriodStart = periodStart;
            RequestedPeriodEndExclusive = periodEndExclusive;
            return Task.FromResult<IReadOnlyList<BackofficeUsageByOrganizationData>>([]);
        }
    }
}
