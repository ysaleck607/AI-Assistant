namespace AssistantCore.Service.Application.Services.RateLimiting;

public interface IOrganizationOrchestrationLeaseStore
{
    Task<OrchestrationLeaseAcquireResult> TryAcquireAsync(
        Guid organizationId,
        int limit,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);
}

public sealed record OrchestrationLeaseAcquireResult(
    IAsyncDisposable? Lease,
    TimeSpan RetryAfter)
{
    public bool IsAcquired => Lease is not null;
}
