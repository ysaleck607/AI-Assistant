using System.Net;
using System.Net.Http.Headers;
using AssistantCore.ExternalServices.Services.Microsoft;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class MicrosoftGraphOutlookMessageDeltaClientTests
{
    [Theory, AutoDomainData]
    public async Task Given_MultiplePages_When_GetInitialPagesAsync_Then_MapsActiveAndDeletedMessagesAndKeepsPaging(
        string accessToken,
        string activeMessageId,
        string deletedMessageId)
    {
        const string deltaLink = "https://graph.microsoft.com/v1.0/users/user/mailFolders/inbox/messages/delta?$deltatoken=next";
        var requestCount = 0;
        var requests = new List<HttpRequestMessage>();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestCount++;
            requests.Add(request);
            return requestCount switch
            {
                1 => CreateResponse($"{{\"value\":[{{\"id\":\"{activeMessageId}\",\"subject\":\"Quarterly results\",\"body\":{{\"content\":\"Revenue increased\"}},\"webLink\":\"https://outlook.office.com/mail/{activeMessageId}\",\"createdDateTime\":\"2026-09-01T10:00:00Z\",\"lastModifiedDateTime\":\"2026-09-01T10:00:00Z\",\"receivedDateTime\":\"2026-09-01T10:00:00Z\"}}],\"@odata.nextLink\":\"https://graph.microsoft.com/v1.0/users/user/mailFolders/inbox/messages/delta?$skiptoken=page-2\"}}"),
                _ => CreateResponse($"{{\"value\":[{{\"id\":\"{deletedMessageId}\",\"@removed\":{{\"reason\":\"deleted\"}}}}],\"@odata.deltaLink\":\"{deltaLink}\"}}")
            };
        }));
        var client = new MicrosoftGraphOutlookMessageDeltaClient(httpClient);

        var pages = new List<AssistantCore.ExternalServices.Entities.Microsoft.MicrosoftOutlookMessageDeltaPage>();
        await foreach (var page in client.GetInitialPagesAsync(
                           "https://graph.microsoft.com",
                           accessToken,
                           "user@contoso.com",
                           "inbox",
                           new DateTimeOffset(2026, 3, 19, 0, 0, 0, TimeSpan.Zero),
                           CancellationToken.None))
        {
            pages.Add(page);
        }

        Assert.Equal(2, requestCount);
        Assert.Equal(2, pages.Count);
        var active = Assert.Single(pages[0].Items);
        Assert.Equal(activeMessageId, active.Id);
        Assert.Equal("Quarterly results", active.Subject);
        Assert.Equal("Revenue increased", active.BodyContent);
        Assert.False(active.IsDeleted);
        var deleted = Assert.Single(pages[1].Items);
        Assert.Equal(deletedMessageId, deleted.Id);
        Assert.True(deleted.IsDeleted);
        Assert.Null(deleted.BodyContent);
        Assert.Equal(deltaLink, pages[1].DeltaLink);
        Assert.Contains("$select=", requests[0].RequestUri?.OriginalString, StringComparison.Ordinal);
        Assert.Contains("$filter=", requests[0].RequestUri?.OriginalString, StringComparison.Ordinal);
        Assert.Contains(
            Uri.EscapeDataString("receivedDateTime ge 2026-03-19T00:00:00.0000000Z"),
            requests[0].RequestUri?.OriginalString,
            StringComparison.Ordinal);
        Assert.All(requests, request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(accessToken, request.Headers.Authorization?.Parameter);
            Assert.Contains(
                request.Headers.GetValues("Prefer"),
                value => value.Contains("outlook.body-content-type", StringComparison.Ordinal));
        });
    }

    [Theory, AutoDomainData]
    public async Task Given_TooManyRequests_When_GetInitialPagesAsync_Then_RetriesAndReturnsFinalDeltaLink(string accessToken)
    {
        const string deltaLink = "https://graph.microsoft.com/v1.0/users/user/mailFolders/inbox/messages/delta?$deltatoken=next";
        var requestCount = 0;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            requestCount++;
            if (requestCount == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("{\"error\":{\"code\":\"tooManyRequests\"}}")
                };
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                return response;
            }

            return CreateResponse($"{{\"value\":[],\"@odata.deltaLink\":\"{deltaLink}\"}}");
        }));
        var client = new MicrosoftGraphOutlookMessageDeltaClient(httpClient);

        var pages = new List<AssistantCore.ExternalServices.Entities.Microsoft.MicrosoftOutlookMessageDeltaPage>();
        await foreach (var page in client.GetInitialPagesAsync(
                           "https://graph.microsoft.com",
                           accessToken,
                           "user-id",
                           "inbox",
                           DateTimeOffset.UtcNow.AddDays(-180),
                           CancellationToken.None))
        {
            pages.Add(page);
        }

        Assert.Equal(2, requestCount);
        Assert.Equal(deltaLink, Assert.Single(pages).DeltaLink);
    }

    [Theory, AutoDomainData]
    public async Task Given_AStoredDeltaLink_When_GetDeltaPagesAsync_Then_RequestsTheExactStoredLink(string accessToken)
    {
        const string storedDeltaLink = "https://graph.microsoft.com/v1.0/users/user/mailFolders/inbox/messages/delta?$deltatoken=opaque%2Bvalue%3D%3D&custom=value";
        const string nextDeltaLink = "https://graph.microsoft.com/v1.0/users/user/mailFolders/inbox/messages/delta?$deltatoken=next";
        Uri? requestUri = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestUri = request.RequestUri;
            return CreateResponse($"{{\"value\":[],\"@odata.deltaLink\":\"{nextDeltaLink}\"}}");
        }));
        var client = new MicrosoftGraphOutlookMessageDeltaClient(httpClient);

        await foreach (var _ in client.GetDeltaPagesAsync(
                           "https://graph.microsoft.com",
                           accessToken,
                           storedDeltaLink,
                           CancellationToken.None))
        {
        }

        Assert.Equal(storedDeltaLink, requestUri?.OriginalString);
    }

    [Theory, AutoDomainData]
    public async Task Given_NoFinalDeltaLink_When_GetInitialPagesAsync_Then_Throws(string accessToken, string messageId)
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            CreateResponse($"{{\"value\":[{{\"id\":\"{messageId}\",\"body\":{{\"content\":\"body\"}}}}]}}")));
        var client = new MicrosoftGraphOutlookMessageDeltaClient(httpClient);

        var action = async () =>
        {
            await foreach (var _ in client.GetInitialPagesAsync(
                               "https://graph.microsoft.com",
                               accessToken,
                               "user-id",
                               "inbox",
                               DateTimeOffset.UtcNow.AddDays(-180),
                               CancellationToken.None))
            {
            }
        };

        await Assert.ThrowsAsync<MicrosoftExternalException>(action);
    }

    private static HttpResponseMessage CreateResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };
}
