using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using Microsoft.Extensions.Caching.Memory;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public sealed class Microsoft365OnboardingCompletionChecker(
    IMicrosoft365ConnectionRepository connectionRepository,
    IMicrosoft365SourceDiscoveryRepository sourceRepository,
    IMemoryCache memoryCache)
    : IMicrosoft365OnboardingCompletionChecker
{
    /// <summary>
    /// L'etat "setup termine" est verifie sur le chemin chaud de l'application
    /// (chaque message, chaque lecture de conversation), mais change rarement
    /// une fois qu'une organisation est configuree. Un court delai de propagation
    /// est sans risque ici : au pire, un membre standard attend quelques secondes
    /// de plus avant d'etre debloque apres qu'un tenantAdmin ait termine la
    /// configuration ; tenantAdmin lui-meme n'est jamais soumis a cette regle.
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

        var isComplete = await ComputeIsCompleteAsync(organizationId, cancellationToken);
        memoryCache.Set(cacheKey, isComplete, CacheDuration);

        return isComplete;
    }

    private async Task<bool> ComputeIsCompleteAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var connection = await connectionRepository.FindByOrganizationAsync(
            organizationId,
            cancellationToken);

        if (connection is null || connection.Status != Microsoft365ConnectionStatus.Active)
        {
            return false;
        }

        var selectedSiteIds = await sourceRepository.GetSiteIdsAsync(
            organizationId,
            cancellationToken);
        if (selectedSiteIds.Count == 0)
        {
            return false;
        }

        return await sourceRepository.IsEnvironmentReadyAsync(
            organizationId,
            cancellationToken);
    }

    private static string GetCacheKey(Guid organizationId) => $"m365-onboarding-complete:{organizationId:D}";
}
