using AssistantCore.Service.Infrastructure.RateLimiting;

namespace AssistantCore.Service.Tests.RateLimiting;

public sealed class InMemoryOrganizationOrchestrationLeaseStoreTests
{
    [Theory, AutoDomainData]
    public async Task Given_AllSlotsAreUsed_When_TryAcquireAsync_Then_DeniesUntilALeaseIsReleased(
        Guid organizationId)
    {
        // Given
        var store = new InMemoryOrganizationOrchestrationLeaseStore(TimeProvider.System);
        var first = await store.TryAcquireAsync(
            organizationId,
            1,
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        // When
        var denied = await store.TryAcquireAsync(
            organizationId,
            1,
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        await first.Lease!.DisposeAsync();
        var allowedAfterRelease = await store.TryAcquireAsync(
            organizationId,
            1,
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        // Then
        Assert.True(first.IsAcquired);
        Assert.False(denied.IsAcquired);
        Assert.True(denied.RetryAfter > TimeSpan.Zero);
        Assert.True(allowedAfterRelease.IsAcquired);
        await allowedAfterRelease.Lease!.DisposeAsync();
    }

    [Theory, AutoDomainData]
    public async Task Given_AReleasedLease_When_DisposeAsync_Then_DoubleReleaseDoesNotFreeAnotherSlot(
        Guid organizationId)
    {
        // Given
        var store = new InMemoryOrganizationOrchestrationLeaseStore(TimeProvider.System);
        var first = await store.TryAcquireAsync(
            organizationId,
            2,
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        var second = await store.TryAcquireAsync(
            organizationId,
            2,
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        // When
        await first.Lease!.DisposeAsync();
        await first.Lease.DisposeAsync();
        var replacement = await store.TryAcquireAsync(
            organizationId,
            2,
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        var extra = await store.TryAcquireAsync(
            organizationId,
            2,
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        // Then
        Assert.True(second.IsAcquired);
        Assert.True(replacement.IsAcquired);
        Assert.False(extra.IsAcquired);
        await second.Lease!.DisposeAsync();
        await replacement.Lease!.DisposeAsync();
    }

    [Theory, AutoDomainData]
    public async Task Given_AnAbandonedLease_When_TryAcquireAsync_Then_RecoversTheSlotAfterExpiry(
        Guid organizationId)
    {
        // Given
        var timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero));
        var store = new InMemoryOrganizationOrchestrationLeaseStore(timeProvider);
        var leaseDuration = TimeSpan.FromMinutes(5);
        var first = await store.TryAcquireAsync(
            organizationId,
            1,
            leaseDuration,
            CancellationToken.None);
        timeProvider.Advance(leaseDuration);

        // When
        var recovered = await store.TryAcquireAsync(
            organizationId,
            1,
            leaseDuration,
            CancellationToken.None);

        // Then
        Assert.True(first.IsAcquired);
        Assert.True(recovered.IsAcquired);
        await recovered.Lease!.DisposeAsync();
    }

    [Theory, AutoDomainData]
    public async Task Given_TwoOrganizations_When_TryAcquireAsync_Then_SlotsRemainIndependent(
        Guid firstOrganizationId,
        Guid secondOrganizationId)
    {
        // Given
        var store = new InMemoryOrganizationOrchestrationLeaseStore(TimeProvider.System);
        var first = await store.TryAcquireAsync(
            firstOrganizationId,
            1,
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        // When
        var firstOrganizationDenied = await store.TryAcquireAsync(
            firstOrganizationId,
            1,
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        var secondOrganizationAllowed = await store.TryAcquireAsync(
            secondOrganizationId,
            1,
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        // Then
        Assert.False(firstOrganizationDenied.IsAcquired);
        Assert.True(secondOrganizationAllowed.IsAcquired);
        await first.Lease!.DisposeAsync();
        await secondOrganizationAllowed.Lease!.DisposeAsync();
    }

    [Theory, AutoDomainData]
    public async Task Given_ConcurrentAcquisitions_When_TryAcquireAsync_Then_DoesNotExceedTheLimit(
        Guid organizationId)
    {
        // Given
        const int limit = 5;
        var store = new InMemoryOrganizationOrchestrationLeaseStore(TimeProvider.System);

        // When
        var results = await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(_ => store.TryAcquireAsync(
                    organizationId,
                    limit,
                    TimeSpan.FromMinutes(5),
                    CancellationToken.None)));

        // Then
        Assert.Equal(limit, results.Count(result => result.IsAcquired));
        Assert.Equal(15, results.Count(result => !result.IsAcquired));
        foreach (var lease in results.Where(result => result.Lease is not null).Select(result => result.Lease!))
        {
            await lease.DisposeAsync();
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
