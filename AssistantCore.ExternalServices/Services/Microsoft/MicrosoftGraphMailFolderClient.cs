using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AssistantCore.ExternalServices.Entities.Microsoft;

namespace AssistantCore.ExternalServices.Services.Microsoft;

public sealed class MicrosoftGraphMailFolderClient(HttpClient httpClient)
{
    private const string MailFolderSelect = "$select=id,displayName,parentFolderId,childFolderCount";
    private readonly MicrosoftGraphCollectionReader collectionReader = new(httpClient);

    public async Task<MicrosoftMailFolder?> GetFolderAsync(
        string graphBaseUrl,
        string accessToken,
        string mailboxUserId,
        string folderId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            CreateFolderUri(graphBaseUrl, mailboxUserId, folderId));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            accessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new MicrosoftExternalException(
                $"Microsoft Graph mail folder lookup failed with status {(int)response.StatusCode}.",
                statusCode: response.StatusCode);
        }

        var folder = await response.Content.ReadFromJsonAsync<MailFolderItem>(cancellationToken)
            ?? throw new MicrosoftExternalException("Microsoft Graph mail folder response was empty.");
        return MapFolder(folder);
    }

    public Task<IReadOnlyCollection<MicrosoftMailFolder>> GetRootFoldersAsync(
        string graphBaseUrl,
        string accessToken,
        string mailboxUserId,
        CancellationToken cancellationToken = default) =>
        collectionReader.ReadAsync<MailFolderItem, MicrosoftMailFolder>(
            CreateFolderCollectionUri(graphBaseUrl, mailboxUserId, parentFolderId: null),
            accessToken,
            MapFolder,
            "mail folders",
            cancellationToken);

    public Task<IReadOnlyCollection<MicrosoftMailFolder>> GetChildFoldersAsync(
        string graphBaseUrl,
        string accessToken,
        string mailboxUserId,
        string parentFolderId,
        CancellationToken cancellationToken = default) =>
        collectionReader.ReadAsync<MailFolderItem, MicrosoftMailFolder>(
            CreateFolderCollectionUri(graphBaseUrl, mailboxUserId, parentFolderId),
            accessToken,
            MapFolder,
            "mail child folders",
            cancellationToken);

    private static Uri CreateFolderUri(
        string graphBaseUrl,
        string mailboxUserId,
        string folderId)
    {
        if (string.IsNullOrWhiteSpace(folderId))
        {
            throw new ArgumentException("Microsoft 365 mail folder identifier is required.", nameof(folderId));
        }

        return new Uri(
            CreateMailboxUri(graphBaseUrl, mailboxUserId),
            $"mailFolders/{Uri.EscapeDataString(folderId)}?{MailFolderSelect}");
    }

    private static Uri CreateFolderCollectionUri(
        string graphBaseUrl,
        string mailboxUserId,
        string? parentFolderId)
    {
        var mailboxUri = CreateMailboxUri(graphBaseUrl, mailboxUserId);
        if (string.IsNullOrWhiteSpace(parentFolderId))
        {
            return new Uri(
                mailboxUri,
                $"mailFolders?{MailFolderSelect}");
        }

        return new Uri(
            mailboxUri,
            $"mailFolders/{Uri.EscapeDataString(parentFolderId)}/childFolders?{MailFolderSelect}");
    }

    private static Uri CreateMailboxUri(string graphBaseUrl, string mailboxUserId)
    {
        if (string.IsNullOrWhiteSpace(mailboxUserId))
        {
            throw new ArgumentException("Microsoft 365 mailbox user identifier is required.", nameof(mailboxUserId));
        }

        if (!Uri.TryCreate(graphBaseUrl, UriKind.Absolute, out var graphBaseUri)
            || graphBaseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Microsoft Graph base URL must use HTTPS.", nameof(graphBaseUrl));
        }

        var normalizedBaseUri = new Uri($"{graphBaseUri.GetLeftPart(UriPartial.Authority)}/");
        return new Uri(
            normalizedBaseUri,
            $"v1.0/users/{Uri.EscapeDataString(mailboxUserId)}/");
    }

    private static MicrosoftMailFolder MapFolder(MailFolderItem folder)
    {
        if (string.IsNullOrWhiteSpace(folder.Id) || string.IsNullOrWhiteSpace(folder.DisplayName))
        {
            throw new MicrosoftExternalException(
                "Microsoft Graph mail folders response contained an invalid folder.");
        }

        return new MicrosoftMailFolder(
            folder.Id,
            folder.DisplayName,
            folder.ParentFolderId,
            Math.Max(0, folder.ChildFolderCount));
    }

    private sealed record MailFolderItem(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("displayName")] string? DisplayName,
        [property: JsonPropertyName("parentFolderId")] string? ParentFolderId,
        [property: JsonPropertyName("childFolderCount")] int ChildFolderCount);
}
