namespace AssistantCore.Service.Application.Configuration;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public int MemberMessagesPerMinute { get; init; } = 10;

    public int OrganizationMessagesPerMinute { get; init; } = 100;

    public int OrganizationConcurrentOrchestrations { get; init; } = 5;

    public int OrchestrationLeaseSeconds { get; init; } = 300;
}
