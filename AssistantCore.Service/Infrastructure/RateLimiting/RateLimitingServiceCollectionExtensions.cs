using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Services.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

namespace AssistantCore.Service.Infrastructure.RateLimiting;

public static class RateLimitingServiceCollectionExtensions
{
    public const string MicrosoftGraphWebhookPolicyName = "MicrosoftGraphWebhook";
    public const string TrustedEdgeClientHeaderName = "X-OnPremia-Edge-Client";

    public static IServiceCollection AddRateLimitingInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IRateLimitStore, InMemoryRateLimitStore>();
        services.AddSingleton<IOrganizationOrchestrationLeaseStore, InMemoryOrganizationOrchestrationLeaseStore>();
        services.AddScoped<IOrganizationOrchestrationLimitService, OrganizationOrchestrationLimitService>();
        services.AddSingleton<IValidateOptions<RateLimitingOptions>, OrchestrationRateLimitingOptionsValidator>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(
                MicrosoftGraphWebhookPolicyName,
                context =>
                {
                    // AssistantCore.Service is internal-only. The public Nginx edge
                    // overwrites this header with its observed remote address before
                    // proxying the Graph webhook, so client-supplied X-Forwarded-For
                    // values are never trusted by the API.
                    var edgeClient = context.Request.Headers[TrustedEdgeClientHeaderName].ToString();
                    var partitionKey = string.IsNullOrWhiteSpace(edgeClient)
                        ? context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
                        : edgeClient;

                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey,
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 120,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        });
                });
        });

        return services;
    }
}
