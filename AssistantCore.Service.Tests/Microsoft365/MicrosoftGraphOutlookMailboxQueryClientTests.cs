using System.Net;
using AssistantCore.ExternalServices.Services.Microsoft;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class MicrosoftGraphOutlookMailboxQueryClientTests
{
    [Theory, AutoDomainData]
    public async Task Given_ARecentReceivedMailQuery_When_QueryAsync_Then_UsesInboxChronologicalOrder()
    {
        // Given
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return CreateResponse(
                "{\"value\":["
                + "{\"id\":\"older\",\"subject\":\"Ancien\",\"bodyPreview\":\"A\",\"receivedDateTime\":\"2026-09-15T10:00:00Z\"},"
                + "{\"id\":\"newest\",\"subject\":\"Nouveau\",\"bodyPreview\":\"B\",\"receivedDateTime\":\"2026-09-17T10:00:00Z\","
                + "\"from\":{\"emailAddress\":{\"name\":\"Giovani\",\"address\":\"giovani@example.com\"}}}]}");
        }));
        var client = new MicrosoftGraphOutlookMailboxQueryClient(httpClient);

        // When
        var messages = await client.QueryAsync(
            "https://graph.microsoft.com",
            "access-token",
            "user-id",
            query: null,
            sender: null,
            recipient: null,
            scope: "received",
            dateFrom: null,
            dateTo: null,
            includeBody: false,
            limit: 1,
            CancellationToken.None);

        // Then
        var message = Assert.Single(messages);
        Assert.Equal("newest", message.Id);
        Assert.Equal("Giovani", message.SenderName);
        Assert.Contains("/mailFolders/inbox/messages?", capturedRequest?.RequestUri?.OriginalString, StringComparison.Ordinal);
        Assert.Contains("$orderby=receivedDateTime%20desc", capturedRequest?.RequestUri?.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$top=1", capturedRequest?.RequestUri?.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Bearer", capturedRequest?.Headers.Authorization?.Scheme);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnOutlookSender_When_QueryAsync_Then_UsesGraphSenderSearch()
    {
        // Given
        Uri? capturedUri = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            capturedUri = request.RequestUri;
            return CreateResponse("{\"value\":[]}");
        }));
        var client = new MicrosoftGraphOutlookMailboxQueryClient(httpClient);

        // When
        var messages = await client.QueryAsync(
            "https://graph.microsoft.com",
            "access-token",
            "user-id",
            query: null,
            sender: "Giovani Tchibozo",
            recipient: null,
            scope: "received",
            dateFrom: null,
            dateTo: null,
            includeBody: false,
            limit: 5,
            CancellationToken.None);

        // Then
        Assert.Empty(messages);
        var decodedUri = Uri.UnescapeDataString(capturedUri?.OriginalString ?? string.Empty);
        Assert.Contains("$search=\"from:Giovani Tchibozo\"", decodedUri, StringComparison.Ordinal);
        Assert.DoesNotContain("$orderby", decodedUri, StringComparison.Ordinal);
    }

    [Theory, AutoDomainData]
    public async Task Given_ExactContentIsRequested_When_QueryAsync_Then_LoadsSelectedBodiesInOneBatch()
    {
        // Given
        var requestCount = 0;
        string? batchPayload = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestCount++;
            if (request.Method == HttpMethod.Post)
            {
                batchPayload = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return CreateResponse(
                    "{\"responses\":[{\"id\":\"0\",\"status\":200,\"body\":{\"body\":{\"content\":\"Montant total : 42,00 $.\"}}}]}");
            }

            return CreateResponse(
                "{\"value\":[{\"id\":\"message-1\",\"subject\":\"Facture\",\"bodyPreview\":\"Votre facture est prete.\","
                + "\"receivedDateTime\":\"2026-09-17T10:00:00Z\"}]}");
        }));
        var client = new MicrosoftGraphOutlookMailboxQueryClient(httpClient);

        // When
        var messages = await client.QueryAsync(
            "https://graph.microsoft.com",
            "access-token",
            "user-id",
            query: "facture",
            sender: "Microsoft",
            recipient: null,
            scope: "received",
            dateFrom: null,
            dateTo: null,
            includeBody: true,
            limit: 1,
            CancellationToken.None);

        // Then
        var message = Assert.Single(messages);
        Assert.Equal("Montant total : 42,00 $.", message.BodyContent);
        Assert.Equal(2, requestCount);
        Assert.Contains("message-1", batchPayload ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("outlook.body-content-type", batchPayload ?? string.Empty, StringComparison.Ordinal);
    }

    private static HttpResponseMessage CreateResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };
}
