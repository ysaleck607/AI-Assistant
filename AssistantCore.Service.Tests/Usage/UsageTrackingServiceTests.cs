using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Services.Usage;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.Usage;

public sealed class UsageTrackingServiceTests
{
    [Theory, AutoDomainData]
    public async Task Given_AMessageProcessedMidMonth_When_RecordConsumptionAsync_Then_PersistsTheMonthlyPeriodBoundaries(
        Guid organizationId,
        Guid assistantMessageId)
    {
        // Given
        var occurredAt = DateTimeOffset.Parse("2026-08-18T15:42:00Z");
        var repository = new StubTokenConsumptionRepository { SumToReturn = 120 };
        var service = CreateService(repository, defaultMonthlyTokenLimit: 1_000_000, now: occurredAt);

        // When
        var response = await service.RecordConsumptionAsync(
            organizationId,
            assistantMessageId,
            100,
            20,
            occurredAt,
            CancellationToken.None);

        // Then
        Assert.NotNull(repository.ReceivedConsumption);
        Assert.Equal(organizationId, repository.ReceivedConsumption.OrganizationId);
        Assert.Equal(assistantMessageId, repository.ReceivedConsumption.AssistantMessageId);
        Assert.Equal(100, repository.ReceivedConsumption.InputTokens);
        Assert.Equal(20, repository.ReceivedConsumption.OutputTokens);
        Assert.Equal(120, repository.ReceivedConsumption.TotalTokens);
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            repository.ReceivedConsumption.PeriodStartsAt);
        Assert.Equal(
            DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
            repository.ReceivedConsumption.PeriodEndsAt);
        Assert.Equal(120, response.RequestTokens);
        Assert.Equal(DateTimeOffset.Parse("2026-09-01T00:00:00Z"), response.PeriodEndsAt);
    }

    [Theory, AutoDomainData]
    public async Task Given_AMessageReplayedAfterAlreadyBeingRecorded_When_RecordConsumptionAsync_Then_StillReturnsTheCurrentTotalsWithoutDoubleCounting(
        Guid organizationId,
        Guid assistantMessageId,
        DateTimeOffset occurredAt)
    {
        // Given
        var repository = new StubTokenConsumptionRepository
        {
            TryRecordResult = false,
            SumToReturn = 300
        };
        var service = CreateService(repository, defaultMonthlyTokenLimit: 1_000_000, now: occurredAt);

        // When
        var response = await service.RecordConsumptionAsync(
            organizationId,
            assistantMessageId,
            100,
            20,
            occurredAt,
            CancellationToken.None);

        // Then
        Assert.Equal(300, response.TokensUsed);
        Assert.Equal(999_700, response.TokensRemaining);
    }

    [Theory, AutoDomainData]
    public async Task Given_UsageBelowTheLimit_When_GetCurrentUsageAsync_Then_ReturnsTheRemainingBalance(
        Guid organizationId)
    {
        // Given
        var now = DateTimeOffset.Parse("2026-08-18T15:42:00Z");
        var repository = new StubTokenConsumptionRepository { SumToReturn = 428_000 };
        var service = CreateService(repository, defaultMonthlyTokenLimit: 1_000_000, now: now);

        // When
        var response = await service.GetCurrentUsageAsync(organizationId, CancellationToken.None);

        // Then
        Assert.Equal(1_000_000, response.TokenLimit);
        Assert.Equal(428_000, response.TokensUsed);
        Assert.Equal(572_000, response.TokensRemaining);
        Assert.False(response.IsExhausted);
    }

    [Theory, AutoDomainData]
    public async Task Given_UsageExceedsTheLimit_When_GetCurrentUsageAsync_Then_ClampsRemainingToZeroAndMarksExhausted(
        Guid organizationId)
    {
        // Given
        var now = DateTimeOffset.Parse("2026-08-18T15:42:00Z");
        var repository = new StubTokenConsumptionRepository { SumToReturn = 1_200_000 };
        var service = CreateService(repository, defaultMonthlyTokenLimit: 1_000_000, now: now);

        // When
        var response = await service.GetCurrentUsageAsync(organizationId, CancellationToken.None);

        // Then
        Assert.Equal(0, response.TokensRemaining);
        Assert.True(response.IsExhausted);
    }

    [Theory, AutoDomainData]
    public async Task Given_UsageExceedsTheLimit_When_EnsureQuotaAvailableAsync_Then_ThrowsWithThePeriodEnd(
        Guid organizationId)
    {
        // Given
        var now = DateTimeOffset.Parse("2026-08-18T15:42:00Z");
        var repository = new StubTokenConsumptionRepository { SumToReturn = 1_000_000 };
        var service = CreateService(repository, defaultMonthlyTokenLimit: 1_000_000, now: now);

        // When
        var exception = await Assert.ThrowsAsync<OrganizationTokenQuotaExceededException>(() =>
            service.EnsureQuotaAvailableAsync(organizationId, CancellationToken.None));

        // Then
        Assert.Equal(
            DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
            exception.PeriodEndsAt);
        Assert.Equal(
            OrganizationTokenQuotaExceededException.Code,
            exception.ErrorCode);
    }

    [Theory, AutoDomainData]
    public async Task Given_TwoOrganizations_When_GetCurrentUsageAsync_Then_QueriesTheRequestedOrganizationOnly(
        Guid organizationId)
    {
        // Given
        var now = DateTimeOffset.Parse("2026-08-18T15:42:00Z");
        var repository = new StubTokenConsumptionRepository { SumToReturn = 0 };
        var service = CreateService(repository, defaultMonthlyTokenLimit: 1_000_000, now: now);

        // When
        await service.GetCurrentUsageAsync(organizationId, CancellationToken.None);

        // Then
        Assert.Equal(organizationId, repository.ReceivedOrganizationId);
        Assert.Equal(DateTimeOffset.Parse("2026-08-01T00:00:00Z"), repository.ReceivedPeriodStartsAt);
        Assert.Equal(DateTimeOffset.Parse("2026-09-01T00:00:00Z"), repository.ReceivedPeriodEndsAt);
    }

    private static UsageTrackingService CreateService(
        ITokenConsumptionRepository repository,
        long defaultMonthlyTokenLimit,
        DateTimeOffset now) =>
        new(
            repository,
            Options.Create(new UsageOptions { DefaultMonthlyTokenLimit = defaultMonthlyTokenLimit }),
            new FixedTimeProvider(now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubTokenConsumptionRepository : ITokenConsumptionRepository
    {
        public bool TryRecordResult { get; init; } = true;

        public long SumToReturn { get; init; }

        public TokenConsumption? ReceivedConsumption { get; private set; }

        public Guid? ReceivedOrganizationId { get; private set; }

        public DateTimeOffset? ReceivedPeriodStartsAt { get; private set; }

        public DateTimeOffset? ReceivedPeriodEndsAt { get; private set; }

        public Task<bool> TryRecordConsumptionAsync(
            TokenConsumption consumption,
            CancellationToken cancellationToken = default)
        {
            ReceivedConsumption = consumption;
            return Task.FromResult(TryRecordResult);
        }

        public Task<long> SumTokensForPeriodAsync(
            Guid organizationId,
            DateTimeOffset periodStartsAt,
            DateTimeOffset periodEndsAt,
            CancellationToken cancellationToken = default)
        {
            ReceivedOrganizationId = organizationId;
            ReceivedPeriodStartsAt = periodStartsAt;
            ReceivedPeriodEndsAt = periodEndsAt;
            return Task.FromResult(SumToReturn);
        }
    }
}
