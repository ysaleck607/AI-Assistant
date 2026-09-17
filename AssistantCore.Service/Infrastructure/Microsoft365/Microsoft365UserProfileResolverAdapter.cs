using AssistantCore.ExternalServices.Services.Microsoft;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Services.Messages.Connectors.Microsoft365;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Infrastructure.Microsoft365;

public sealed class Microsoft365UserProfileResolverAdapter(
    MicrosoftIdentityClient identityClient,
    MicrosoftGraphUserProfileClient profileClient,
    IOptions<Microsoft365Options> options,
    IMemoryCache? memoryCache = null) : IMicrosoft365UserProfileResolver
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public async Task<Microsoft365UserProfile?> ResolveAsync(
        string externalTenantId,
        string entraUserId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalTenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entraUserId);

        var cacheKey = new UserProfileCacheKey(
            externalTenantId.Trim().ToLowerInvariant(),
            entraUserId.Trim().ToLowerInvariant());
        if (memoryCache is not null
            && memoryCache.TryGetValue(cacheKey, out Microsoft365UserProfile? cachedProfile))
        {
            return cachedProfile;
        }

        var configuration = options.Value;
        var token = await identityClient.AcquireApplicationTokenAsync(
            configuration.AuthorityBaseUrl,
            externalTenantId,
            configuration.ClientId,
            configuration.ClientSecret,
            cancellationToken);
        var graphProfile = await profileClient.GetAsync(
            configuration.GraphBaseUrl,
            token.AccessToken,
            entraUserId,
            cancellationToken);

        // A deleted guest object or a member whose invitation was revoked resolves to
        // no profile at all; callers must treat that as no access, never fall back to a
        // cached or borrowed identity.
        var profile = graphProfile is null
            ? null
            : new Microsoft365UserProfile(
                string.Equals(graphProfile.UserType, "Guest", StringComparison.OrdinalIgnoreCase),
                graphProfile.UserPrincipalName);

        memoryCache?.Set(cacheKey, profile, CacheDuration);
        return profile;
    }

    private sealed record UserProfileCacheKey(
        string TenantId,
        string UserId);
}
