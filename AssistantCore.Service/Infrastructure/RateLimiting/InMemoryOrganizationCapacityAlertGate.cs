using AssistantCore.Service.Application.Services.RateLimiting;

namespace AssistantCore.Service.Infrastructure.RateLimiting;

public sealed class InMemoryOrganizationCapacityAlertGate(TimeProvider timeProvider)
    : IOrganizationCapacityAlertGate
{
    private static readonly TimeSpan CooldownWindow = TimeSpan.FromMinutes(5);

    private readonly object _syncRoot = new();
    private readonly Dictionary<Guid, DateTimeOffset> _nextAllowedAt = [];

    public bool TryAcquire(Guid organizationId)
    {
        var now = timeProvider.GetUtcNow();

        lock (_syncRoot)
        {
            if (_nextAllowedAt.TryGetValue(organizationId, out var nextAllowedAt) && now < nextAllowedAt)
            {
                return false;
            }

            _nextAllowedAt[organizationId] = now.Add(CooldownWindow);
            return true;
        }
    }
}
