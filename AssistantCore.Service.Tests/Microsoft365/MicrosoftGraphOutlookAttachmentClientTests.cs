using System.Net;
using System.Net.Http.Headers;
using AssistantCore.ExternalServices.Services.Microsoft;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class MicrosoftGraphOutlookAttachmentClientTests
{
    [Theory, AutoDomainData]
    public async Task Given_AFileAttachment_When_GetFileAttachmentsAsync_Then_DownloadsItsContentWithBearerAuthorization(
        string accessToken)
    {
        // Given
        var authorizations = new List<AuthenticationHeaderValue?>();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            authorizations.Add(request.Headers.Authorization);
            return request.RequestUri?.AbsolutePath.EndsWith("/attachments", StringComparison.Ordinal) == true
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"value\":[{\"id\":\"attachment-id\",\"name\":\"budget.txt\",\"contentType\":\"text/plain\",\"size\":7,\"isInline\":false,\"@odata.type\":\"#microsoft.graph.fileAttachment\"}]}" )
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"contentBytes\":\"YnVkZ2V0\"}")
                };
        }));
        var client = new MicrosoftGraphOutlookAttachmentClient(httpClient);

        // When
        var attachments = await client.GetFileAttachmentsAsync(
            "https://graph.microsoft.com",
            accessToken,
            "mailbox-id",
            "message-id",
            1024,
            CancellationToken.None);

        // Then
        var attachment = Assert.Single(attachments);
        Assert.Equal("budget.txt", attachment.Name);
        Assert.Equal("budget", System.Text.Encoding.UTF8.GetString(attachment.Content));
        Assert.All(authorizations, authorization =>
        {
            Assert.Equal("Bearer", authorization?.Scheme);
            Assert.Equal(accessToken, authorization?.Parameter);
        });
    }
}
