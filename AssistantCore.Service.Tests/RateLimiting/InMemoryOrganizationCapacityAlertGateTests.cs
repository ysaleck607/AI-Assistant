using AssistantCore.Service.Infrastructure.RateLimiting;

namespace AssistantCore.Service.Tests.RateLimiting;

public sealed class InMemoryOrganizationCapacityAlertGateTests
{
    [Theory, AutoDomainData]
    public void Given_AFreshOrganization_When_TryAcquire_Then_AllowsTheFirstCall(Guid organizationId)
    {
        // Given
        var gate = new InMemoryOrganizationCapacityAlertGate(
            new MutableTimeProvider(new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero)));

        // When
        var acquired = gate.TryAcquire(organizationId);

        // Then
        Assert.True(acquired);
    }

    [Theory, AutoDomainData]
    public void Given_AnOrganizationAlreadyAlerted_When_TryAcquireWithinCooldown_Then_DeniesFurtherCalls(
        Guid organizationId)
    {
        // Given
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero));
        var gate = new InMemoryOrganizationCapacityAlertGate(timeProvider);
        gate.TryAcquire(organizationId);

        // When
        timeProvider.Advance(TimeSpan.FromMinutes(4));
        var acquiredAgain = gate.TryAcquire(organizationId);

        // Then
        Assert.False(acquiredAgain);
    }

    [Theory, AutoDomainData]
    public void Given_AnOrganizationAlreadyAlerted_When_TryAcquireAfterCooldown_Then_AllowsAgain(
        Guid organizationId)
    {
        // Given
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero));
        var gate = new InMemoryOrganizationCapacityAlertGate(timeProvider);
        gate.TryAcquire(organizationId);

        // When
        timeProvider.Advance(TimeSpan.FromMinutes(5));
        var acquiredAgain = gate.TryAcquire(organizationId);

        // Then
        Assert.True(acquiredAgain);
    }

    [Theory, AutoDomainData]
    public void Given_TwoDifferentOrganizations_When_TryAcquire_Then_EachHasItsOwnCooldown(
        Guid organizationA,
        Guid organizationB)
    {
        // Given
        var gate = new InMemoryOrganizationCapacityAlertGate(
            new MutableTimeProvider(new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero)));
        gate.TryAcquire(organizationA);

        // When
        var acquiredForOtherOrganization = gate.TryAcquire(organizationB);

        // Then
        Assert.True(acquiredForOtherOrganization);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
