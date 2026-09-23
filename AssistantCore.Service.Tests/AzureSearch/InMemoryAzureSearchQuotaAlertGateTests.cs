using AssistantCore.Service.Infrastructure.AzureSearch;

namespace AssistantCore.Service.Tests.AzureSearch;

public sealed class InMemoryAzureSearchQuotaAlertGateTests
{
    [Fact]
    public void Given_AFreshGate_When_TryAcquire_Then_AllowsTheFirstCall()
    {
        // Given
        var gate = new InMemoryAzureSearchQuotaAlertGate(
            new MutableTimeProvider(new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero)));

        // When
        var acquired = gate.TryAcquire();

        // Then
        Assert.True(acquired);
    }

    [Fact]
    public void Given_AnAlertAlreadySent_When_TryAcquireWithinCooldown_Then_DeniesFurtherCalls()
    {
        // Given
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero));
        var gate = new InMemoryAzureSearchQuotaAlertGate(timeProvider);
        gate.TryAcquire();

        // When
        timeProvider.Advance(TimeSpan.FromHours(5));
        var acquiredAgain = gate.TryAcquire();

        // Then
        Assert.False(acquiredAgain);
    }

    [Fact]
    public void Given_AnAlertAlreadySent_When_TryAcquireAfterCooldown_Then_AllowsAgain()
    {
        // Given
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero));
        var gate = new InMemoryAzureSearchQuotaAlertGate(timeProvider);
        gate.TryAcquire();

        // When
        timeProvider.Advance(TimeSpan.FromHours(6));
        var acquiredAgain = gate.TryAcquire();

        // Then
        Assert.True(acquiredAgain);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
