using AssistantCore.ExternalServices.Services.Microsoft;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Services.Messages.Connectors.Microsoft365;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Infrastructure.Microsoft365;

public sealed class Microsoft365SharePointGroupResolverAdapter(
    IMicrosoft365SourceDiscoveryRepository sourceRepository,
    IMicrosoft365UserProfileResolver profileResolver,
    MicrosoftCertificateIdentityClient identityClient,
    MicrosoftSharePointUserGroupClient groupClient,
    IMicrosoft365SecurityIdentityNormalizer identityNormalizer,
    IMemoryCache memoryCache,
    IOptions<Microsoft365Options> options,
    ILogger<Microsoft365SharePointGroupResolverAdapter> logger) : IMicrosoft365SharePointGroupResolver
{
    public async Task<IReadOnlyCollection<string>> ResolveGroupIdsAsync(
        Guid organizationId,
        string externalTenantId,
        string entraUserId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(organizationId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalTenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entraUserId);

        var configuration = options.Value;
        if ((string.IsNullOrWhiteSpace(configuration.SharePointCertificatePath)
                && string.IsNullOrWhiteSpace(configuration.SharePointCertificateBase64))
            || string.IsNullOrWhiteSpace(configuration.SharePointCertificatePassword))
        {
            return [];
        }

        // SharePoint's local-group API only accepts a user principal name, never an Entra
        // Object ID. The JWT email claim cannot stand in for it: a guest's client-tenant UPN
        // differs from their home-tenant email, so it is resolved from the Object ID here
        // instead of trusting a caller-supplied email. No profile (deleted guest, revoked
        // invitation) means no groups, never a stale or borrowed identity.
        var profile = await profileResolver.ResolveAsync(externalTenantId, entraUserId, cancellationToken);
        if (profile is null)
        {
            logger.LogWarning(
                "Microsoft SharePoint group resolution skipped: no Microsoft Graph profile for the authenticated user.");
            return [];
        }

        var sites = await sourceRepository.GetIndexedSitesAsync(
            organizationId,
            cancellationToken);
        var normalizedGroupIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var site in sites)
        {
            IReadOnlyCollection<string> siteGroupIds;
            try
            {
                siteGroupIds = await ResolveSiteGroupIdsAsync(
                    externalTenantId,
                    entraUserId,
                    profile.UserPrincipalName,
                    site,
                    configuration,
                    cancellationToken);
            }
            catch (MicrosoftExternalException exception)
            {
                logger.LogWarning(
                    exception,
                    "Microsoft SharePoint group resolution failed for site {SiteId}; continuing without this site's groups.",
                    site.SiteId);
                continue;
            }

            normalizedGroupIds.UnionWith(siteGroupIds);
        }

        return normalizedGroupIds.OrderBy(groupId => groupId, StringComparer.Ordinal).ToArray();
    }

    private async Task<IReadOnlyCollection<string>> ResolveSiteGroupIdsAsync(
        string externalTenantId,
        string entraUserId,
        string userPrincipalName,
        Microsoft365SharePointSiteData site,
        Microsoft365Options configuration,
        CancellationToken cancellationToken)
    {
        var cacheKey = new SharePointGroupCacheKey(
            externalTenantId.Trim().ToLowerInvariant(),
            entraUserId.Trim().ToLowerInvariant(),
            site.SiteId);
        var cached = await memoryCache.GetOrCreateAsync(
            cacheKey,
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(
                    configuration.SharePointGroupCacheMinutes);
                var siteUri = new Uri(site.WebUrl, UriKind.Absolute);
                var scope = $"{siteUri.GetLeftPart(UriPartial.Authority)}/.default";
                var token = string.IsNullOrWhiteSpace(configuration.SharePointCertificateBase64)
                    ? await identityClient.AcquireApplicationTokenForScopeAsync(
                        configuration.AuthorityBaseUrl,
                        externalTenantId,
                        configuration.ClientId,
                        configuration.SharePointCertificatePath,
                        configuration.SharePointCertificatePassword,
                        scope,
                        cancellationToken)
                    : await identityClient.AcquireApplicationTokenFromBase64ForScopeAsync(
                        configuration.AuthorityBaseUrl,
                        externalTenantId,
                        configuration.ClientId,
                        configuration.SharePointCertificateBase64,
                        configuration.SharePointCertificatePassword,
                        scope,
                        cancellationToken);
                var groupIds = await groupClient.GetGroupIdsAsync(
                    site.WebUrl,
                    token.AccessToken,
                    userPrincipalName,
                    cancellationToken);

                return (IReadOnlyCollection<string>)groupIds
                    .Select(groupId => identityNormalizer.NormalizeSharePointGroupId(
                        site.SiteId,
                        groupId))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(groupId => groupId, StringComparer.Ordinal)
                    .ToArray();
            });

        return cached
            ?? throw new InvalidOperationException(
                "Microsoft SharePoint local group resolution returned no result.");
    }

    private sealed record SharePointGroupCacheKey(
        string TenantId,
        string EntraUserId,
        string SiteId);
}
