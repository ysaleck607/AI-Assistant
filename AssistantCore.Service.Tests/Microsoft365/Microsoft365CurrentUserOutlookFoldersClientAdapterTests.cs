using System.Net;
using AssistantCore.ExternalServices.Services.Microsoft;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Services.Microsoft365;
using AssistantCore.Service.Infrastructure.Microsoft365;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365CurrentUserOutlookFoldersClientAdapterTests
{
    [Theory, AutoDomainData]
    public async Task Given_MailFoldersWithExcludedRoots_When_GetIndexableFoldersAsync_Then_ReturnsAllowedFoldersRecursively(
        string accessToken)
    {
        // Given
        var requestedUris = new List<string>();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri?.OriginalString ?? string.Empty);
            var uri = request.RequestUri?.OriginalString ?? string.Empty;
            if (uri.Contains("/mailFolders/deleteditems?", StringComparison.Ordinal))
            {
                return CreateResponse(
                    "{\"id\":\"deleted-folder-id\",\"displayName\":\"Elements supprimes\",\"parentFolderId\":\"root\",\"childFolderCount\":1}");
            }

            if (uri.Contains("/mailFolders/junkemail?", StringComparison.Ordinal))
            {
                return CreateResponse(
                    "{\"id\":\"junk-folder-id\",\"displayName\":\"Courrier indesirable\",\"parentFolderId\":\"root\",\"childFolderCount\":0}");
            }

            if (uri.Contains("/mailFolders/archive/childFolders", StringComparison.Ordinal))
            {
                return CreateResponse(
                    "{\"value\":[{\"id\":\"archive-child\",\"displayName\":\"Project\",\"parentFolderId\":\"archive\",\"childFolderCount\":0}]}");
            }

            return CreateResponse(
                "{\"value\":["
                + "{\"id\":\"inbox\",\"displayName\":\"Inbox\",\"parentFolderId\":\"root\",\"childFolderCount\":0},"
                + "{\"id\":\"sentitems\",\"displayName\":\"Sent Items\",\"parentFolderId\":\"root\",\"childFolderCount\":0},"
                + "{\"id\":\"archive\",\"displayName\":\"Archive\",\"parentFolderId\":\"root\",\"childFolderCount\":1},"
                + "{\"id\":\"deleted-folder-id\",\"displayName\":\"Elements supprimes\",\"parentFolderId\":\"root\",\"childFolderCount\":1},"
                + "{\"id\":\"junk-folder-id\",\"displayName\":\"Courrier indesirable\",\"parentFolderId\":\"root\",\"childFolderCount\":0}"
                + "]}");
        }));
        var adapter = new Microsoft365CurrentUserOutlookFoldersClientAdapter(
            new MicrosoftGraphMailFolderClient(httpClient),
            new StubApplicationTokenClient(accessToken),
            Options.Create(new Microsoft365Options { GraphBaseUrl = "https://graph.microsoft.com" }));

        // When
        var folders = await adapter.GetIndexableFoldersAsync(
            "tenant-id",
            "user-id",
            CancellationToken.None);

        // Then
        Assert.Equal(
            ["Archive", "Archive/Project", "Inbox", "Sent Items"],
            folders.Select(folder => folder.DisplayPath).Order().ToArray());
        Assert.DoesNotContain(folders, folder => folder.FolderId == "deleted-folder-id");
        Assert.DoesNotContain(folders, folder => folder.FolderId == "junk-folder-id");
        Assert.DoesNotContain(
            requestedUris,
            uri => uri.Contains("/mailFolders/deleted-folder-id/childFolders", StringComparison.Ordinal));
    }

    private static HttpResponseMessage CreateResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };

    private sealed class StubApplicationTokenClient(string accessToken) : IMicrosoft365ApplicationTokenClient
    {
        public Task<string> AcquireGraphTokenAsync(
            string tenantId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(accessToken);
    }
}
