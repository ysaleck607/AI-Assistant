using AssistantCore.Service.Application.Services.LlmQuota;

namespace AssistantCore.Service.Infrastructure.LlmQuota;

public sealed class InMemoryLlmQuotaAlertGate(TimeProvider timeProvider) : ILlmQuotaAlertGate
{
    private static readonly TimeSpan CooldownWindow = TimeSpan.FromHours(6);

    private readonly object _syncRoot = new();
    private readonly Dictionary<string, DateTimeOffset> _nextAllowedAt = [];

    public bool TryAcquire(string model)
    {
        var now = timeProvider.GetUtcNow();

        lock (_syncRoot)
        {
            if (_nextAllowedAt.TryGetValue(model, out var nextAllowedAt) && now < nextAllowedAt)
            {
                return false;
            }

            _nextAllowedAt[model] = now.Add(CooldownWindow);
            return true;
        }
    }
}
