using AssistantCore.Service.Application.Services.AzureSearch;

namespace AssistantCore.Service.Infrastructure.AzureSearch;

public sealed class InMemoryAzureSearchQuotaAlertGate(TimeProvider timeProvider) : IAzureSearchQuotaAlertGate
{
    private static readonly TimeSpan CooldownWindow = TimeSpan.FromHours(6);

    private readonly object _syncRoot = new();
    private DateTimeOffset? _nextAllowedAt;

    public bool TryAcquire()
    {
        var now = timeProvider.GetUtcNow();

        lock (_syncRoot)
        {
            if (_nextAllowedAt is { } nextAllowedAt && now < nextAllowedAt)
            {
                return false;
            }

            _nextAllowedAt = now.Add(CooldownWindow);
            return true;
        }
    }
}
