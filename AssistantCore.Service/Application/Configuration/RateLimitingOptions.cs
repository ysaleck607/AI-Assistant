namespace AssistantCore.Service.Application.Configuration;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public int MemberMessagesPerMinute { get; init; } = 10;

    public int OrganizationMessagesPerMinute { get; init; } = 100;
}
