using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Messages;
using AssistantCore.Service.Application.Services.RateLimiting;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.RateLimiting;

public sealed class MessageRateLimitServiceTests
{
    [Theory, AutoDomainData]
    public async Task Given_OrganizationAndMemberAreBelowTheirLimits_When_EnsureAllowedAsync_Then_AcquiresBothRulesTogether(
        MessageUserContext userContext)
    {
        // Given
        var store = new StubRateLimitStore(new RateLimitAcquireResult(true, TimeSpan.Zero));
        var service = CreateService(store);

        // When
        await service.EnsureAllowedAsync(userContext, CancellationToken.None);

        // Then
        Assert.Equal(2, store.ReceivedRules.Count);
        Assert.Contains(
            store.ReceivedRules,
            rule => rule.Key == $"rate:member:{userContext.Organization.Id:N}:{userContext.Member.Id:N}:messages"
                && rule.Limit == 10
                && rule.Window == TimeSpan.FromMinutes(1));
        Assert.Contains(
            store.ReceivedRules,
            rule => rule.Key == $"rate:organization:{userContext.Organization.Id:N}:messages"
                && rule.Limit == 100
                && rule.Window == TimeSpan.FromMinutes(1));
        Assert.Equal(1, store.CallCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnyRateLimitIsReached_When_EnsureAllowedAsync_Then_ThrowsWithRoundedRetryAfter(
        MessageUserContext userContext)
    {
        // Given
        var store = new StubRateLimitStore(
            new RateLimitAcquireResult(false, TimeSpan.FromSeconds(12.2)));
        var service = CreateService(store);

        // When
        var exception = await Assert.ThrowsAsync<RequestRateLimitExceededException>(() =>
            service.EnsureAllowedAsync(userContext, CancellationToken.None));

        // Then
        Assert.Equal(RequestRateLimitExceededException.Code, exception.ErrorCode);
        Assert.Equal(13, exception.RetryAfterSeconds);
        Assert.Equal(1, store.CallCount);
        Assert.Equal(2, store.ReceivedRules.Count);
    }

    private static MessageRateLimitService CreateService(IRateLimitStore store) =>
        new(
            store,
            Options.Create(new RateLimitingOptions
            {
                MemberMessagesPerMinute = 10,
                OrganizationMessagesPerMinute = 100
            }));

    private sealed class StubRateLimitStore(RateLimitAcquireResult result) : IRateLimitStore
    {
        public int CallCount { get; private set; }

        public IReadOnlyCollection<RateLimitRule> ReceivedRules { get; private set; } = [];

        public Task<RateLimitAcquireResult> TryAcquireAsync(
            IReadOnlyCollection<RateLimitRule> rules,
            CancellationToken cancellationToken)
        {
            CallCount++;
            ReceivedRules = rules;
            return Task.FromResult(result);
        }
    }
}
