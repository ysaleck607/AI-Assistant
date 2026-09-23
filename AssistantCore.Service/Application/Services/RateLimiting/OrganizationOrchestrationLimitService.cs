using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Application.Services.RateLimiting;

public sealed class OrganizationOrchestrationLimitService(
    IOrganizationOrchestrationLeaseStore store,
    IOptions<RateLimitingOptions> options) : IOrganizationOrchestrationLimitService
{
    private readonly RateLimitingOptions _options = options.Value;

    public async Task<IAsyncDisposable> AcquireAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var result = await store.TryAcquireAsync(
            organizationId,
            _options.OrganizationConcurrentOrchestrations,
            TimeSpan.FromSeconds(_options.OrchestrationLeaseSeconds),
            cancellationToken);

        if (result.Lease is not null)
        {
            return result.Lease;
        }

        var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(result.RetryAfter.TotalSeconds));
        throw new RequestRateLimitExceededException(retryAfterSeconds);
    }
}
