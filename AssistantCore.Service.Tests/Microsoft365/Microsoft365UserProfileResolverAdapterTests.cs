using System.Net;
using AssistantCore.ExternalServices.Services.Microsoft;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Infrastructure.Microsoft365;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365UserProfileResolverAdapterTests
{
    [Theory, AutoDomainData]
    public async Task Given_AGuestGraphProfile_When_ResolveAsync_Then_ReturnsIsGuestTrue(
        string externalTenantId,
        Guid entraUserId,
        string userPrincipalName)
    {
        // Given
        var resolver = CreateResolver(new MemoryCache(new MemoryCacheOptions()), _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"id":"{{entraUserId:D}}","userType":"Guest","userPrincipalName":"{{userPrincipalName}}"}""")
            });

        // When
        var profile = await resolver.ResolveAsync(externalTenantId, entraUserId.ToString("D"), CancellationToken.None);

        // Then
        Assert.NotNull(profile);
        Assert.True(profile!.IsGuest);
        Assert.Equal(userPrincipalName, profile.UserPrincipalName);
    }

    [Theory, AutoDomainData]
    public async Task Given_AMemberGraphProfile_When_ResolveAsync_Then_ReturnsIsGuestFalse(
        string externalTenantId,
        Guid entraUserId,
        string userPrincipalName)
    {
        // Given
        var resolver = CreateResolver(new MemoryCache(new MemoryCacheOptions()), _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"id":"{{entraUserId:D}}","userType":"Member","userPrincipalName":"{{userPrincipalName}}"}""")
            });

        // When
        var profile = await resolver.ResolveAsync(externalTenantId, entraUserId.ToString("D"), CancellationToken.None);

        // Then
        Assert.NotNull(profile);
        Assert.False(profile!.IsGuest);
    }

    [Theory, AutoDomainData]
    public async Task Given_ADeletedOrUnassignedUser_When_ResolveAsync_Then_ReturnsNullWithoutCachingAPositiveResult(
        string externalTenantId,
        Guid entraUserId)
    {
        // Given
        var graphCallCount = 0;
        var resolver = CreateResolver(new MemoryCache(new MemoryCacheOptions()), _ =>
        {
            graphCallCount++;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        // When
        var profile = await resolver.ResolveAsync(externalTenantId, entraUserId.ToString("D"), CancellationToken.None);

        // Then
        Assert.Null(profile);
        Assert.Equal(1, graphCallCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_ARepeatedLookupWithinTheCacheWindow_When_ResolveAsync_Then_OnlyQueriesGraphOnce(
        string externalTenantId,
        Guid entraUserId,
        string userPrincipalName)
    {
        // Given
        var graphCallCount = 0;
        var resolver = CreateResolver(new MemoryCache(new MemoryCacheOptions()), _ =>
        {
            graphCallCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"id":"{{entraUserId:D}}","userType":"Guest","userPrincipalName":"{{userPrincipalName}}"}""")
            };
        });

        // When
        var first = await resolver.ResolveAsync(externalTenantId, entraUserId.ToString("D"), CancellationToken.None);
        var second = await resolver.ResolveAsync(externalTenantId, entraUserId.ToString("D"), CancellationToken.None);

        // Then
        Assert.Equal(1, graphCallCount);
        Assert.Equal(first, second);
    }

    private static Microsoft365UserProfileResolverAdapter CreateResolver(
        IMemoryCache memoryCache,
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        var handler = new StubHttpMessageHandler(request =>
            request.Method == HttpMethod.Post ? TokenResponse() : responseFactory(request));
        var identityClient = new MicrosoftIdentityClient(new HttpClient(handler));
        var profileClient = new MicrosoftGraphUserProfileClient(new HttpClient(handler));
        var options = Options.Create(new Microsoft365Options
        {
            AuthorityBaseUrl = "https://login.microsoftonline.com",
            GraphBaseUrl = "https://graph.microsoft.com",
            ClientId = Guid.NewGuid().ToString("D"),
            ClientSecret = "client-secret"
        });

        return new Microsoft365UserProfileResolverAdapter(identityClient, profileClient, options, memoryCache);
    }

    private static HttpResponseMessage TokenResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("""{"access_token":"access-token","expires_in":3600}""")
    };
}
