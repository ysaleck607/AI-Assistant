using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Usage;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Application.Services.Usage;

public sealed class UsageTrackingService(
    ITokenConsumptionRepository consumptionRepository,
    IOptions<UsageOptions> options,
    TimeProvider timeProvider) : IUsageTrackingService
{
    public async Task EnsureQuotaAvailableAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var usage = await GetCurrentUsageAsync(organizationId, cancellationToken);
        if (usage.IsExhausted)
        {
            throw new OrganizationTokenQuotaExceededException(usage.PeriodEndsAt);
        }
    }

    public async Task<MessageUsageResponse> RecordConsumptionAsync(
        Guid organizationId,
        Guid assistantMessageId,
        long inputTokens,
        long outputTokens,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        var (periodStartsAt, periodEndsAt) = ComputeMonthlyPeriod(occurredAt);
        var requestTokens = inputTokens + outputTokens;

        var consumption = new TokenConsumption
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            AssistantMessageId = assistantMessageId,
            PeriodStartsAt = periodStartsAt,
            PeriodEndsAt = periodEndsAt,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = requestTokens,
            CreatedAt = occurredAt
        };

        await consumptionRepository.TryRecordConsumptionAsync(consumption, cancellationToken);

        var (tokenLimit, tokensUsed, tokensRemaining) = await SummarizePeriodAsync(
            organizationId,
            periodStartsAt,
            periodEndsAt,
            cancellationToken);

        return new MessageUsageResponse(
            requestTokens,
            tokenLimit,
            tokensUsed,
            tokensRemaining,
            periodEndsAt,
            tokensRemaining == 0);
    }

    public async Task<TokenUsageResponse> GetCurrentUsageAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var (periodStartsAt, periodEndsAt) = ComputeMonthlyPeriod(timeProvider.GetUtcNow());
        var (tokenLimit, tokensUsed, tokensRemaining) = await SummarizePeriodAsync(
            organizationId,
            periodStartsAt,
            periodEndsAt,
            cancellationToken);

        return new TokenUsageResponse(
            periodStartsAt,
            periodEndsAt,
            tokenLimit,
            tokensUsed,
            tokensRemaining,
            tokensRemaining == 0);
    }

    private async Task<(long TokenLimit, long TokensUsed, long TokensRemaining)> SummarizePeriodAsync(
        Guid organizationId,
        DateTimeOffset periodStartsAt,
        DateTimeOffset periodEndsAt,
        CancellationToken cancellationToken)
    {
        var tokensUsed = await consumptionRepository.SumTokensForPeriodAsync(
            organizationId,
            periodStartsAt,
            periodEndsAt,
            cancellationToken);
        var tokenLimit = options.Value.DefaultMonthlyTokenLimit;
        var tokensRemaining = Math.Max(0, tokenLimit - tokensUsed);

        return (tokenLimit, tokensUsed, tokensRemaining);
    }

    /// <summary>
    /// Periode mensuelle UTC : le debut est inclus, la fin est exclue et represente
    /// le renouvellement. Provisoire tant que #108 n'introduit pas de politiques de
    /// quota versionnees avec une date de renouvellement propre a l'organisation.
    /// </summary>
    private static (DateTimeOffset Start, DateTimeOffset End) ComputeMonthlyPeriod(DateTimeOffset now)
    {
        var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return (start, start.AddMonths(1));
    }
}
