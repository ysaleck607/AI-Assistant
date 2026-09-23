using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AssistantCore.ExternalServices.Entities.Microsoft;

namespace AssistantCore.ExternalServices.Services.Microsoft;

public sealed class MicrosoftGraphUserProfileClient(HttpClient httpClient)
{
    public async Task<MicrosoftUserProfile?> GetAsync(
        string graphBaseUrl,
        string accessToken,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(userId, out var parsedUserId) || parsedUserId == Guid.Empty)
        {
            throw new ArgumentException("A valid Microsoft Entra user identifier is required.", nameof(userId));
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{graphBaseUrl.TrimEnd('/')}/v1.0/users/{parsedUserId:D}?$select=id,userType,userPrincipalName");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new MicrosoftExternalException(
                $"Microsoft Graph user profile lookup failed with status {(int)response.StatusCode}.",
                statusCode: response.StatusCode);
        }

        var profile = await response.Content.ReadFromJsonAsync<UserResponse>(cancellationToken)
            ?? throw new MicrosoftExternalException("Microsoft Graph returned an empty user profile response.");

        if (string.IsNullOrWhiteSpace(profile.UserPrincipalName))
        {
            throw new MicrosoftExternalException("Microsoft Graph returned a user profile without a user principal name.");
        }

        return new MicrosoftUserProfile(
            profile.Id,
            profile.UserType ?? "Member",
            profile.UserPrincipalName);
    }

    private sealed record UserResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("userType")] string? UserType,
        [property: JsonPropertyName("userPrincipalName")] string? UserPrincipalName);
}
