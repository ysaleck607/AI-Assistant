using System.Diagnostics.Metrics;
using AssistantCore.Service.Application.Services.RateLimiting;

namespace AssistantCore.Service.Infrastructure.RateLimiting;

public sealed class InMemoryOrganizationOrchestrationLeaseStore(TimeProvider timeProvider)
    : IOrganizationOrchestrationLeaseStore
{
    private static readonly Meter Meter = new("AssistantCore.RateLimiting");
    private static readonly Counter<long> RejectedCounter =
        Meter.CreateCounter<long>("orchestration.acquire.rejected");
    private static readonly Counter<long> ExpiredLeaseCounter =
        Meter.CreateCounter<long>("orchestration.leases.expired");
    private static readonly UpDownCounter<long> ActiveLeaseCounter =
        Meter.CreateUpDownCounter<long>("orchestration.leases.active");

    private readonly object _syncRoot = new();
    private readonly Dictionary<Guid, Dictionary<Guid, DateTimeOffset>> _leasesByOrganization = [];

    public Task<OrchestrationLeaseAcquireResult> TryAcquireAsync(
        Guid organizationId,
        int limit,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow();

        lock (_syncRoot)
        {
            var leases = GetOrCreateOrganizationLeases(organizationId);
            RemoveExpiredLeases(leases, now);

            if (leases.Count >= limit)
            {
                RejectedCounter.Add(1);
                var retryAfter = leases.Values.Min() - now;
                return Task.FromResult(
                    new OrchestrationLeaseAcquireResult(
                        Lease: null,
                        RetryAfter: retryAfter > TimeSpan.Zero ? retryAfter : TimeSpan.FromSeconds(1)));
            }

            var leaseId = Guid.NewGuid();
            leases.Add(leaseId, now.Add(leaseDuration));
            ActiveLeaseCounter.Add(1);

            IAsyncDisposable lease = new InMemoryLease(this, organizationId, leaseId);
            return Task.FromResult(
                new OrchestrationLeaseAcquireResult(
                    Lease: lease,
                    RetryAfter: TimeSpan.Zero));
        }
    }

    private Dictionary<Guid, DateTimeOffset> GetOrCreateOrganizationLeases(Guid organizationId)
    {
        if (_leasesByOrganization.TryGetValue(organizationId, out var leases))
        {
            return leases;
        }

        leases = [];
        _leasesByOrganization.Add(organizationId, leases);
        return leases;
    }

    private static void RemoveExpiredLeases(
        Dictionary<Guid, DateTimeOffset> leases,
        DateTimeOffset now)
    {
        var expiredLeaseIds = leases
            .Where(entry => entry.Value <= now)
            .Select(entry => entry.Key)
            .ToArray();

        foreach (var leaseId in expiredLeaseIds)
        {
            leases.Remove(leaseId);
            ActiveLeaseCounter.Add(-1);
            ExpiredLeaseCounter.Add(1);
        }
    }

    private void Release(Guid organizationId, Guid leaseId)
    {
        lock (_syncRoot)
        {
            if (!_leasesByOrganization.TryGetValue(organizationId, out var leases)
                || !leases.Remove(leaseId))
            {
                return;
            }

            ActiveLeaseCounter.Add(-1);
            if (leases.Count == 0)
            {
                _leasesByOrganization.Remove(organizationId);
            }
        }
    }

    private sealed class InMemoryLease(
        InMemoryOrganizationOrchestrationLeaseStore owner,
        Guid organizationId,
        Guid leaseId) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                owner.Release(organizationId, leaseId);
            }

            return ValueTask.CompletedTask;
        }
    }
}
