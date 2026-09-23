using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AssistantCore.ExternalServices.Services.Microsoft;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class MicrosoftGraphSubscriptionClientTests
{
    [Theory, AutoDomainData]
    public async Task Given_ASubscriptionRequest_When_CreateAsync_Then_SendsProtectedGraphRequest(
        string subscriptionId,
        string accessToken,
        string clientState,
        DateTimeOffset expiresAt)
    {
        // Given
        HttpMethod? method = null;
        AuthenticationHeaderValue? authorization = null;
        string? prefer = null;
        string? requestBody = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            method = request.Method;
            authorization = request.Headers.Authorization;
            prefer = request.Headers.GetValues("Prefer").Single();
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(
                HttpStatusCode.Created,
                new
                {
                    id = subscriptionId,
                    resource = "/drives/drive-id/root",
                    expirationDateTime = expiresAt
                });
        }));
        var client = new MicrosoftGraphSubscriptionClient(httpClient);

        // When
        var result = await client.CreateAsync(
            "https://graph.microsoft.com",
            accessToken,
            "/drives/drive-id/root",
            "https://assistant.example/webhooks/microsoft-graph",
            expiresAt,
            clientState,
            "updated",
            CancellationToken.None);

        // Then
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("Bearer", authorization?.Scheme);
        Assert.Equal(accessToken, authorization?.Parameter);
        Assert.Equal("includesecuritywebhooks", prefer);
        Assert.Equal(subscriptionId, result.Id);
        using var json = JsonDocument.Parse(requestBody!);
        Assert.Equal(clientState, json.RootElement.GetProperty("clientState").GetString());
        Assert.Equal("updated", json.RootElement.GetProperty("changeType").GetString());
    }

    [Theory, AutoDomainData]
    public async Task Given_AListSubscriptionRequest_When_CreateAsync_Then_SecurityWebhookPreferenceIsNotSent(
        string subscriptionId,
        string accessToken,
        string clientState,
        DateTimeOffset expiresAt)
    {
        // Given
        bool? hasPreferHeader = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            hasPreferHeader = request.Headers.Contains("Prefer");
            return JsonResponse(
                HttpStatusCode.Created,
                new
                {
                    id = subscriptionId,
                    resource = "/sites/site-id/lists/list-id",
                    expirationDateTime = expiresAt
                });
        }));
        var client = new MicrosoftGraphSubscriptionClient(httpClient);

        // When
        await client.CreateAsync(
            "https://graph.microsoft.com",
            accessToken,
            "/sites/site-id/lists/list-id",
            "https://assistant.example/webhooks/microsoft-graph",
            expiresAt,
            clientState,
            "updated",
            CancellationToken.None);

        // Then
        Assert.False(hasPreferHeader);
    }

    [Theory, AutoDomainData]
    public async Task Given_AGraphErrorResponse_When_CreateAsync_Then_ThrowsSanitizedGraphDetails(
        string accessToken,
        string clientState,
        DateTimeOffset expiresAt)
    {
        // Given
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            JsonResponse(
                HttpStatusCode.BadRequest,
                new
                {
                    error = new
                    {
                        code = "InvalidRequest",
                        message = "Webhook validation\r\nfailed."
                    }
                })));
        var client = new MicrosoftGraphSubscriptionClient(httpClient);

        // When
        var action = () => client.CreateAsync(
            "https://graph.microsoft.com",
            accessToken,
            "/sites/site-id/lists/list-id",
            "https://assistant.example/webhooks/microsoft-graph",
            expiresAt,
            clientState,
            "updated",
            CancellationToken.None);

        // Then
        var exception = await Assert.ThrowsAsync<MicrosoftExternalException>(action);
        Assert.Equal("InvalidRequest", exception.ErrorCode);
        Assert.Contains("Graph error InvalidRequest:", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Webhook validation  failed.", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', exception.Message);
        Assert.DoesNotContain('\n', exception.Message);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnExistingSubscription_When_RenewAsync_Then_SendsNotificationUrlAndNewExpiration(
        string subscriptionId,
        string accessToken,
        string notificationUrl,
        DateTimeOffset expiresAt)
    {
        // Given
        HttpRequestMessage? capturedRequest = null;
        string? requestBody = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(
                HttpStatusCode.OK,
                new
                {
                    id = subscriptionId,
                    resource = "/drives/drive-id/root",
                    expirationDateTime = expiresAt
                });
        }));
        var client = new MicrosoftGraphSubscriptionClient(httpClient);

        // When
        var result = await client.RenewAsync(
            "https://graph.microsoft.com",
            accessToken,
            subscriptionId,
            notificationUrl,
            expiresAt,
            CancellationToken.None);

        // Then
        Assert.Equal(HttpMethod.Patch, capturedRequest?.Method);
        Assert.EndsWith($"/subscriptions/{subscriptionId}", capturedRequest?.RequestUri?.AbsolutePath);
        using var json = JsonDocument.Parse(requestBody!);
        Assert.Equal(notificationUrl, json.RootElement.GetProperty("notificationUrl").GetString());
        Assert.Equal(2, json.RootElement.EnumerateObject().Count());
        Assert.Equal(expiresAt, result.ExpiresAt);
    }

    [Theory, AutoDomainData]
    public async Task Given_AMissingSubscription_When_RenewAsync_Then_ReportsNotFound(
        string subscriptionId,
        string accessToken,
        string notificationUrl,
        DateTimeOffset expiresAt)
    {
        // Given
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)));
        var client = new MicrosoftGraphSubscriptionClient(httpClient);

        // When
        var action = () => client.RenewAsync(
            "https://graph.microsoft.com",
            accessToken,
            subscriptionId,
            notificationUrl,
            expiresAt,
            CancellationToken.None);

        // Then
        var exception = await Assert.ThrowsAsync<MicrosoftExternalException>(action);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnAlreadyDeletedSubscription_When_DeleteAsync_Then_CompletesSuccessfully(
        string subscriptionId,
        string accessToken)
    {
        // Given
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)));
        var client = new MicrosoftGraphSubscriptionClient(httpClient);

        // When
        var exception = await Record.ExceptionAsync(() => client.DeleteAsync(
            "https://graph.microsoft.com",
            accessToken,
            subscriptionId,
            CancellationToken.None));

        // Then
        Assert.Null(exception);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object value) =>
        new(statusCode)
        {
            Content = new StringContent(JsonSerializer.Serialize(value))
        };
}
