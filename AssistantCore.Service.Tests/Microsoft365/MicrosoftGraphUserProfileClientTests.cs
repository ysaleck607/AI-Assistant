using System.Net;
using System.Net.Http.Headers;
using AssistantCore.ExternalServices.Services.Microsoft;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class MicrosoftGraphUserProfileClientTests
{
    [Theory, AutoDomainData]
    public async Task Given_AGuestUser_When_GetAsync_Then_ReturnsGuestUserTypeAndUpn(
        Guid userId,
        string userPrincipalName,
        string accessToken)
    {
        // Given
        AuthenticationHeaderValue? capturedAuthorization = null;
        Uri? capturedUri = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            capturedAuthorization = request.Headers.Authorization;
            capturedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"id":"{{userId:D}}","userType":"Guest","userPrincipalName":"{{userPrincipalName}}"}""")
            };
        }));
        var client = new MicrosoftGraphUserProfileClient(httpClient);

        // When
        var profile = await client.GetAsync(
            "https://graph.microsoft.com",
            accessToken,
            userId.ToString("D"),
            CancellationToken.None);

        // Then
        Assert.NotNull(profile);
        Assert.Equal("Guest", profile!.UserType);
        Assert.Equal(userPrincipalName, profile.UserPrincipalName);
        Assert.Equal("Bearer", capturedAuthorization?.Scheme);
        Assert.Equal(accessToken, capturedAuthorization?.Parameter);
        Assert.Contains($"/v1.0/users/{userId:D}", capturedUri!.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("userType,userPrincipalName", capturedUri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Theory, AutoDomainData]
    public async Task Given_AMemberWithoutExplicitUserType_When_GetAsync_Then_DefaultsToMember(
        Guid userId,
        string userPrincipalName,
        string accessToken)
    {
        // Given
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"id":"{{userId:D}}","userPrincipalName":"{{userPrincipalName}}"}""")
            }));
        var client = new MicrosoftGraphUserProfileClient(httpClient);

        // When
        var profile = await client.GetAsync(
            "https://graph.microsoft.com",
            accessToken,
            userId.ToString("D"),
            CancellationToken.None);

        // Then
        Assert.NotNull(profile);
        Assert.Equal("Member", profile!.UserType);
    }

    [Theory, AutoDomainData]
    public async Task Given_ADeletedOrUnknownUser_When_GetAsync_Then_ReturnsNull(
        Guid userId,
        string accessToken)
    {
        // Given
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)));
        var client = new MicrosoftGraphUserProfileClient(httpClient);

        // When
        var profile = await client.GetAsync(
            "https://graph.microsoft.com",
            accessToken,
            userId.ToString("D"),
            CancellationToken.None);

        // Then
        Assert.Null(profile);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnInvalidUserId_When_GetAsync_Then_ThrowsArgumentException(
        string accessToken)
    {
        // Given
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)));
        var client = new MicrosoftGraphUserProfileClient(httpClient);

        // When
        var action = () => client.GetAsync(
            "https://graph.microsoft.com",
            accessToken,
            "not-a-guid",
            CancellationToken.None);

        // Then
        await Assert.ThrowsAsync<ArgumentException>(action);
    }

    [Theory, AutoDomainData]
    public async Task Given_AServerError_When_GetAsync_Then_ThrowsMicrosoftExternalException(
        Guid userId,
        string accessToken)
    {
        // Given
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var client = new MicrosoftGraphUserProfileClient(httpClient);

        // When
        var action = () => client.GetAsync(
            "https://graph.microsoft.com",
            accessToken,
            userId.ToString("D"),
            CancellationToken.None);

        // Then
        await Assert.ThrowsAsync<MicrosoftExternalException>(action);
    }
}
