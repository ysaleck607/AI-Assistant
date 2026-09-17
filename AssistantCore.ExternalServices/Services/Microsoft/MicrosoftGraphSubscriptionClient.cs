using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssistantCore.ExternalServices.Entities.Microsoft;

namespace AssistantCore.ExternalServices.Services.Microsoft;

public sealed class MicrosoftGraphSubscriptionClient(HttpClient httpClient)
{
    public async Task<MicrosoftGraphSubscription> CreateAsync(
        string graphBaseUrl,
        string accessToken,
        string resource,
        string notificationUrl,
        DateTimeOffset expiresAt,
        string clientState,
        string changeType,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"{graphBaseUrl.TrimEnd('/')}/v1.0/subscriptions",
            accessToken);
        if (resource.TrimStart('/').StartsWith("drives/", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.TryAddWithoutValidation("Prefer", "includesecuritywebhooks");
        }
        request.Content = JsonContent.Create(new CreateSubscriptionRequest(
            ChangeType: changeType,
            NotificationUrl: notificationUrl,
            Resource: resource,
            ExpirationDateTime: expiresAt,
            ClientState: clientState));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadSubscriptionAsync(response, "creation", cancellationToken);
    }

    public async Task<MicrosoftGraphSubscription> RenewAsync(
        string graphBaseUrl,
        string accessToken,
        string subscriptionId,
        string notificationUrl,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationUrl);
        using var request = CreateAuthorizedRequest(
            HttpMethod.Patch,
            $"{graphBaseUrl.TrimEnd('/')}/v1.0/subscriptions/{Uri.EscapeDataString(subscriptionId)}",
            accessToken);
        request.Content = JsonContent.Create(new RenewSubscriptionRequest(
            notificationUrl,
            expiresAt));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadSubscriptionAsync(response, "renewal", cancellationToken);
    }

    public async Task DeleteAsync(
        string graphBaseUrl,
        string accessToken,
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"{graphBaseUrl.TrimEnd('/')}/v1.0/subscriptions/{Uri.EscapeDataString(subscriptionId)}",
            accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return;
        }

        throw new MicrosoftExternalException(
            $"Microsoft Graph subscription deletion failed with status {(int)response.StatusCode}.",
            statusCode: response.StatusCode);
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string requestUri,
        string accessToken)
    {
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static async Task<MicrosoftGraphSubscription> ReadSubscriptionAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var graphError = await ReadGraphErrorAsync(response, cancellationToken);
            var graphErrorDetails = (graphError.Code, graphError.Message) switch
            {
                (not null, not null) => $"Graph error {graphError.Code}: {graphError.Message}",
                (not null, null) => $"Graph error {graphError.Code}.",
                (null, not null) => graphError.Message,
                _ => null
            };
            throw new MicrosoftExternalException(
                graphErrorDetails is null
                    ? $"Microsoft Graph subscription {operation} failed with status {(int)response.StatusCode}."
                    : $"Microsoft Graph subscription {operation} failed with status {(int)response.StatusCode}. {graphErrorDetails}",
                statusCode: response.StatusCode,
                errorCode: graphError.Code);
        }

        var payload = await response.Content.ReadFromJsonAsync<SubscriptionResponse>(cancellationToken)
            ?? throw new MicrosoftExternalException(
                $"Microsoft Graph subscription {operation} response was empty.");
        if (string.IsNullOrWhiteSpace(payload.Id)
            || string.IsNullOrWhiteSpace(payload.Resource)
            || payload.ExpirationDateTime == default)
        {
            throw new MicrosoftExternalException(
                $"Microsoft Graph subscription {operation} response was invalid.");
        }

        return new MicrosoftGraphSubscription(
            payload.Id,
            payload.Resource,
            payload.ExpirationDateTime);
    }

    private sealed record CreateSubscriptionRequest(
        [property: JsonPropertyName("changeType")] string ChangeType,
        [property: JsonPropertyName("notificationUrl")] string NotificationUrl,
        [property: JsonPropertyName("resource")] string Resource,
        [property: JsonPropertyName("expirationDateTime")] DateTimeOffset ExpirationDateTime,
        [property: JsonPropertyName("clientState")] string ClientState);

    private sealed record RenewSubscriptionRequest(
        [property: JsonPropertyName("notificationUrl")] string NotificationUrl,
        [property: JsonPropertyName("expirationDateTime")] DateTimeOffset ExpirationDateTime);

    private sealed record SubscriptionResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("resource")] string Resource,
        [property: JsonPropertyName("expirationDateTime")] DateTimeOffset ExpirationDateTime);

    private static async Task<GraphError> ReadGraphErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<GraphErrorResponse>(cancellationToken);
            return new GraphError(
                Sanitize(payload?.Error?.Code, 100),
                Sanitize(payload?.Error?.Message, 1000));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return new GraphError(null, null);
        }
    }

    private static string? Sanitize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var sanitized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return sanitized.Length <= maximumLength
            ? sanitized
            : sanitized[..maximumLength];
    }

    private sealed record GraphErrorResponse(
        [property: JsonPropertyName("error")] GraphError? Error);

    private sealed record GraphError(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("message")] string? Message);
}
