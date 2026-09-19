using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Application.Configuration;

public sealed class OrchestrationRateLimitingOptionsValidator(
    IOptions<AgentRuntimeOptions> agentRuntimeOptions) : IValidateOptions<RateLimitingOptions>
{
    public ValidateOptionsResult Validate(string? name, RateLimitingOptions options)
    {
        if (options.OrganizationConcurrentOrchestrations <= 0)
        {
            return ValidateOptionsResult.Fail(
                $"{RateLimitingOptions.SectionName}:{nameof(RateLimitingOptions.OrganizationConcurrentOrchestrations)} must be greater than zero.");
        }

        if (options.OrchestrationLeaseSeconds <= 0)
        {
            return ValidateOptionsResult.Fail(
                $"{RateLimitingOptions.SectionName}:{nameof(RateLimitingOptions.OrchestrationLeaseSeconds)} must be greater than zero.");
        }

        if (options.OrchestrationLeaseSeconds <= agentRuntimeOptions.Value.MaximumExecutionTimeSeconds)
        {
            return ValidateOptionsResult.Fail(
                $"{RateLimitingOptions.SectionName}:{nameof(RateLimitingOptions.OrchestrationLeaseSeconds)} must be greater than Messages:AgentRuntime:{nameof(AgentRuntimeOptions.MaximumExecutionTimeSeconds)}.");
        }

        return ValidateOptionsResult.Success;
    }
}
