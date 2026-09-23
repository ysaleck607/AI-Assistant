using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Services.LlmQuota;

namespace AssistantCore.Service.Tests.LlmQuota;

public sealed class LlmTokenConsumptionTrackerTests
{
    [Fact]
    public async Task Given_AMidMonthTimestamp_When_RecordConsumptionAsync_Then_UsesTheFirstDayOfTheMonthAsThePeriod()
    {
        // Given
        var repository = new RecordingLlmTokenConsumptionRepository();
        var now = new DateTimeOffset(2026, 9, 21, 14, 30, 0, TimeSpan.Zero);
        var tracker = new LlmTokenConsumptionTracker(repository, new StubTimeProvider(now));

        // When
        await tracker.RecordConsumptionAsync("gpt-5.5", 120, CancellationToken.None);

        // Then
        var recorded = Assert.Single(repository.RecordedCalls);
        Assert.Equal("gpt-5.5", recorded.Model);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), recorded.PeriodStart);
        Assert.Equal(120, recorded.Tokens);
        Assert.Equal(now, recorded.Now);
    }

    [Fact]
    public async Task Given_AMidMonthTimestamp_When_GetCurrentPeriodConsumptionAsync_Then_QueriesTheFirstDayOfTheMonth()
    {
        // Given
        var repository = new RecordingLlmTokenConsumptionRepository { ConsumptionToReturn = 4_200 };
        var now = new DateTimeOffset(2026, 9, 21, 14, 30, 0, TimeSpan.Zero);
        var tracker = new LlmTokenConsumptionTracker(repository, new StubTimeProvider(now));

        // When
        var consumption = await tracker.GetCurrentPeriodConsumptionAsync("text-embedding-3-small", CancellationToken.None);

        // Then
        Assert.Equal(4_200, consumption);
        var query = Assert.Single(repository.QueriedPeriods);
        Assert.Equal("text-embedding-3-small", query.Model);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), query.PeriodStart);
    }

    private sealed class StubTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingLlmTokenConsumptionRepository : ILlmTokenConsumptionRepository
    {
        public long ConsumptionToReturn { get; set; }

        public List<(string Model, DateTimeOffset PeriodStart, long Tokens, DateTimeOffset Now)> RecordedCalls { get; } = [];

        public List<(string Model, DateTimeOffset PeriodStart)> QueriedPeriods { get; } = [];

        public Task RecordConsumptionAsync(
            string model,
            DateTimeOffset periodStart,
            long tokens,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            RecordedCalls.Add((model, periodStart, tokens, now));
            return Task.CompletedTask;
        }

        public Task<long> GetConsumptionAsync(
            string model,
            DateTimeOffset periodStart,
            CancellationToken cancellationToken = default)
        {
            QueriedPeriods.Add((model, periodStart));
            return Task.FromResult(ConsumptionToReturn);
        }
    }
}
