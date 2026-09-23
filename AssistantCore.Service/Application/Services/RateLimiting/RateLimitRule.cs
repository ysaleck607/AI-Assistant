namespace AssistantCore.Service.Application.Services.RateLimiting;

public sealed record RateLimitRule(
    string Key,
    int Limit,
    TimeSpan Window);
