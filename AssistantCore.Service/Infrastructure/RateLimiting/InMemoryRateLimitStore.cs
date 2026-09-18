using AssistantCore.Service.Application.Services.RateLimiting;

namespace AssistantCore.Service.Infrastructure.RateLimiting;

public sealed class InMemoryRateLimitStore(TimeProvider timeProvider) : IRateLimitStore
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(1);

    private readonly object _syncRoot = new();
    private readonly Dictionary<string, WindowCounter> _counters = [];
    private DateTimeOffset _nextCleanupAt = DateTimeOffset.MinValue;

    public Task<RateLimitAcquireResult> TryAcquireAsync(
        IReadOnlyCollection<RateLimitRule> rules,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(rules);

        if (rules.Count == 0)
        {
            return Task.FromResult(new RateLimitAcquireResult(true, TimeSpan.Zero));
        }

        var now = timeProvider.GetUtcNow();

        lock (_syncRoot)
        {
            RemoveExpiredCountersIfDue(now);

            var counters = rules
                .Select(rule => new RuleCounter(rule, GetOrCreateCounter(rule, now)))
                .ToArray();

            var retryAfter = counters
                .Where(item => item.Counter.Count >= item.Rule.Limit)
                .Select(item => item.Counter.ResetsAt - now)
                .DefaultIfEmpty(TimeSpan.Zero)
                .Max();

            if (retryAfter > TimeSpan.Zero)
            {
                return Task.FromResult(
                    new RateLimitAcquireResult(
                        IsAllowed: false,
                        RetryAfter: retryAfter));
            }

            foreach (var item in counters)
            {
                item.Counter.Count++;
            }

            return Task.FromResult(
                new RateLimitAcquireResult(
                    IsAllowed: true,
                    RetryAfter: TimeSpan.Zero));
        }
    }

    private WindowCounter GetOrCreateCounter(RateLimitRule rule, DateTimeOffset now)
    {
        if (_counters.TryGetValue(rule.Key, out var counter)
            && counter.ResetsAt > now)
        {
            return counter;
        }

        counter = new WindowCounter(now.Add(rule.Window));
        _counters[rule.Key] = counter;
        return counter;
    }

    private void RemoveExpiredCountersIfDue(DateTimeOffset now)
    {
        if (now < _nextCleanupAt)
        {
            return;
        }

        foreach (var key in _counters
                     .Where(entry => entry.Value.ResetsAt <= now)
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            _counters.Remove(key);
        }

        _nextCleanupAt = now.Add(CleanupInterval);
    }

    private sealed class WindowCounter(DateTimeOffset resetsAt)
    {
        public int Count { get; set; }

        public DateTimeOffset ResetsAt { get; } = resetsAt;
    }

    private sealed record RuleCounter(
        RateLimitRule Rule,
        WindowCounter Counter);
}
