namespace AssistantCore.Service.Application.Services.RateLimiting;

public interface IRateLimitStore
{
    Task<RateLimitAcquireResult> TryAcquireAsync(
        IReadOnlyCollection<RateLimitRule> rules,
        CancellationToken cancellationToken);
}
