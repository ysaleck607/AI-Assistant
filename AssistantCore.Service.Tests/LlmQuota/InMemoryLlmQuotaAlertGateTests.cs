using AssistantCore.Service.Infrastructure.LlmQuota;

namespace AssistantCore.Service.Tests.LlmQuota;

public sealed class InMemoryLlmQuotaAlertGateTests
{
    [Fact]
    public void Given_AFreshModel_When_TryAcquire_Then_AllowsTheFirstCall()
    {
        // Given
        var gate = new InMemoryLlmQuotaAlertGate(
            new MutableTimeProvider(new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero)));

        // When
        var acquired = gate.TryAcquire("gpt-5.5");

        // Then
        Assert.True(acquired);
    }

    [Fact]
    public void Given_AModelAlreadyAlerted_When_TryAcquireWithinCooldown_Then_DeniesFurtherCalls()
    {
        // Given
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero));
        var gate = new InMemoryLlmQuotaAlertGate(timeProvider);
        gate.TryAcquire("gpt-5.5");

        // When
        timeProvider.Advance(TimeSpan.FromHours(5));
        var acquiredAgain = gate.TryAcquire("gpt-5.5");

        // Then
        Assert.False(acquiredAgain);
    }

    [Fact]
    public void Given_TwoDifferentModels_When_TryAcquire_Then_EachHasItsOwnCooldown()
    {
        // Given
        var gate = new InMemoryLlmQuotaAlertGate(
            new MutableTimeProvider(new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero)));
        gate.TryAcquire("gpt-5.5");

        // When
        var acquiredForOtherModel = gate.TryAcquire("text-embedding-3-small");

        // Then
        Assert.True(acquiredForOtherModel);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
