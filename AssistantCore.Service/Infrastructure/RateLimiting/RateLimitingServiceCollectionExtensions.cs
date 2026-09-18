using AssistantCore.Service.Application.Services.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AssistantCore.Service.Infrastructure.RateLimiting;

public static class RateLimitingServiceCollectionExtensions
{
    public static IServiceCollection AddRateLimitingInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IRateLimitStore, InMemoryRateLimitStore>();
        return services;
    }
}
