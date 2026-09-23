using AssistantCore.Service.Application.Services.RateLimiting;
using AssistantCore.Service.Infrastructure.RateLimiting;

namespace AssistantCore.Service.Tests.RateLimiting;

public sealed class InMemoryRateLimitStoreTests
{
    [Theory, AutoDomainData]
    public async Task Given_AFullWindow_When_TryAcquireAsync_Then_DeniesUntilWindowExpires(
        string key)
    {
        // Given
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero));
        var store = new InMemoryRateLimitStore(timeProvider);
        var rule = new RateLimitRule(key, 2, TimeSpan.FromMinutes(1));
        await store.TryAcquireAsync([rule], CancellationToken.None);
        await store.TryAcquireAsync([rule], CancellationToken.None);

        // When
        var denied = await store.TryAcquireAsync([rule], CancellationToken.None);
        timeProvider.Advance(rule.Window);
        var allowedAfterReset = await store.TryAcquireAsync([rule], CancellationToken.None);

        // Then
        Assert.False(denied.IsAllowed);
        Assert.Equal(rule.Window, denied.RetryAfter);
        Assert.True(allowedAfterReset.IsAllowed);
    }

    [Theory, AutoDomainData]
    public async Task Given_ConcurrentRequests_When_TryAcquireAsync_Then_DoesNotExceedTheLimit(
        string key)
    {
        // Given
        const int limit = 10;
        var store = new InMemoryRateLimitStore(TimeProvider.System);
        var rules = new[] { new RateLimitRule(key, limit, TimeSpan.FromMinutes(1)) };

        // When
        var results = await Task.WhenAll(
            Enumerable.Range(0, 25)
                .Select(_ => store.TryAcquireAsync(rules, CancellationToken.None)));

        // Then
        Assert.Equal(limit, results.Count(result => result.IsAllowed));
        Assert.Equal(15, results.Count(result => !result.IsAllowed));
    }

    [Theory, AutoDomainData]
    public async Task Given_OneRuleIsAlreadyFull_When_TryAcquireAsync_Then_DoesNotConsumeTheOtherRule(
        string memberKey,
        string organizationKey)
    {
        // Given
        var store = new InMemoryRateLimitStore(TimeProvider.System);
        var memberRule = new RateLimitRule(memberKey, 1, TimeSpan.FromMinutes(1));
        var organizationRule = new RateLimitRule(organizationKey, 1, TimeSpan.FromMinutes(1));
        await store.TryAcquireAsync([organizationRule], CancellationToken.None);

        // When
        var denied = await store.TryAcquireAsync(
            [memberRule, organizationRule],
            CancellationToken.None);
        var memberStillAvailable = await store.TryAcquireAsync(
            [memberRule],
            CancellationToken.None);

        // Then
        Assert.False(denied.IsAllowed);
        Assert.True(memberStillAvailable.IsAllowed);
    }

    [Theory, AutoDomainData]
    public async Task Given_MultipleFullRules_When_TryAcquireAsync_Then_ReturnsTheLongestRetryAfter(
        string firstKey,
        string secondKey)
    {
        // Given
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero));
        var store = new InMemoryRateLimitStore(timeProvider);
        var shortRule = new RateLimitRule(firstKey, 1, TimeSpan.FromSeconds(20));
        var longRule = new RateLimitRule(secondKey, 1, TimeSpan.FromSeconds(45));
        await store.TryAcquireAsync([shortRule, longRule], CancellationToken.None);

        // When
        var denied = await store.TryAcquireAsync([shortRule, longRule], CancellationToken.None);

        // Then
        Assert.False(denied.IsAllowed);
        Assert.Equal(longRule.Window, denied.RetryAfter);
    }

    [Theory, AutoDomainData]
    public async Task Given_DifferentMembers_When_TryAcquireAsync_Then_MemberCountersRemainIndependent(
        string firstMemberKey,
        string secondMemberKey)
    {
        // Given
        var store = new InMemoryRateLimitStore(TimeProvider.System);
        var firstMemberRule = new RateLimitRule(firstMemberKey, 1, TimeSpan.FromMinutes(1));
        var secondMemberRule = new RateLimitRule(secondMemberKey, 1, TimeSpan.FromMinutes(1));
        await store.TryAcquireAsync([firstMemberRule], CancellationToken.None);

        // When
        var firstMemberDenied = await store.TryAcquireAsync([firstMemberRule], CancellationToken.None);
        var secondMemberAllowed = await store.TryAcquireAsync([secondMemberRule], CancellationToken.None);

        // Then
        Assert.False(firstMemberDenied.IsAllowed);
        Assert.True(secondMemberAllowed.IsAllowed);
    }

    [Theory, AutoDomainData]
    public async Task Given_TwoMembersShareAnOrganization_When_TryAcquireAsync_Then_OrganizationCounterIsShared(
        string firstMemberKey,
        string secondMemberKey,
        string organizationKey)
    {
        // Given
        var store = new InMemoryRateLimitStore(TimeProvider.System);
        var firstMemberRule = new RateLimitRule(firstMemberKey, 1, TimeSpan.FromMinutes(1));
        var secondMemberRule = new RateLimitRule(secondMemberKey, 1, TimeSpan.FromMinutes(1));
        var organizationRule = new RateLimitRule(organizationKey, 1, TimeSpan.FromMinutes(1));
        await store.TryAcquireAsync([firstMemberRule, organizationRule], CancellationToken.None);

        // When
        var secondMemberDenied = await store.TryAcquireAsync(
            [secondMemberRule, organizationRule],
            CancellationToken.None);
        var secondMemberCounterStillAvailable = await store.TryAcquireAsync(
            [secondMemberRule],
            CancellationToken.None);

        // Then
        Assert.False(secondMemberDenied.IsAllowed);
        Assert.True(secondMemberCounterStillAvailable.IsAllowed);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
