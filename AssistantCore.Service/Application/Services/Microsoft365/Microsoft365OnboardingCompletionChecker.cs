using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using Microsoft.Extensions.Caching.Memory;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public sealed class Microsoft365OnboardingCompletionChecker(
    IMicrosoft365ConnectionRepository connectionRepository,
    IMemoryCache memoryCache)
    : IMicrosoft365OnboardingCompletionChecker
{
    /// <summary>
    /// Le setup termine est un etat de cycle de vie persistant. Il ne doit pas
    /// redevenir incomplet a cause d'une indexation temporairement indisponible
    /// ou parce que toutes les sources ont ensuite ete deselectionnees.
    /// </summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public async Task<bool> IsCompleteAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey(organizationId);

        if (memoryCache.TryGetValue(cacheKey, out bool cachedIsComplete))
        {
            return cachedIsComplete;
        }

        var connection = await connectionRepository.FindByOrganizationAsync(
            organizationId,
            cancellationToken);
        var isComplete = connection is not null
            && connection.Status == Microsoft365ConnectionStatus.Active
            && connection.OnboardingCompletedAt is not null;

        memoryCache.Set(cacheKey, isComplete, CacheDuration);
        return isComplete;
    }

    private static string GetCacheKey(Guid organizationId) =>
        $"m365-onboarding-complete:{organizationId:D}";
}
