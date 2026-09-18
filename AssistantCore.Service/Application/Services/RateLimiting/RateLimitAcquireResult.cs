namespace AssistantCore.Service.Application.Services.RateLimiting;

public sealed record RateLimitAcquireResult(
    bool IsAllowed,
    TimeSpan RetryAfter);
