using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssistantCore.ExternalServices.Entities.Microsoft;

namespace AssistantCore.ExternalServices.Services.Microsoft;

public sealed class MicrosoftGraphOutlookMessageDeltaClient(HttpClient httpClient)
{
    private static readonly string[] MessagePreferences =
        ["outlook.body-content-type=\"text\""];
    private readonly MicrosoftGraphCollectionReader collectionReader = new(httpClient);

    public async IAsyncEnumerable<MicrosoftOutlookMessageDeltaPage> GetInitialPagesAsync(
        string graphBaseUrl,
        string accessToken,
        string mailboxUserId,
        string mailFolderId,
        DateTimeOffset receivedSince,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var page in GetPagesAsync(
                           CreateInitialDeltaUri(graphBaseUrl, mailboxUserId, mailFolderId, receivedSince),
                           accessToken,
                           cancellationToken))
        {
            yield return page;
        }
    }

    public async IAsyncEnumerable<MicrosoftOutlookMessageDeltaPage> GetDeltaPagesAsync(
        string graphBaseUrl,
        string accessToken,
        string deltaLink,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var page in GetPagesAsync(
                           CreateStoredDeltaUri(graphBaseUrl, deltaLink),
                           accessToken,
                           cancellationToken))
        {
            yield return page;
        }
    }

    private async IAsyncEnumerable<MicrosoftOutlookMessageDeltaPage> GetPagesAsync(
        Uri firstPageUri,
        string accessToken,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var foundFinalDeltaLink = false;
        await foreach (var page in collectionReader.ReadPagesAsync<Message, MicrosoftOutlookMessageDelta>(
                           firstPageUri,
                           accessToken,
                           MapMessage,
                           "Outlook message delta",
                           cancellationToken,
                           MessagePreferences))
        {
            foundFinalDeltaLink |= page.DeltaLink is not null;
            yield return new MicrosoftOutlookMessageDeltaPage(page.Items, page.DeltaLink);
        }

        if (!foundFinalDeltaLink)
        {
            throw new MicrosoftExternalException(
                "Microsoft Graph Outlook message delta response did not contain a final delta link.");
        }
    }

    private static Uri CreateInitialDeltaUri(
        string graphBaseUrl,
        string mailboxUserId,
        string mailFolderId,
        DateTimeOffset receivedSince)
    {
        if (string.IsNullOrWhiteSpace(mailboxUserId))
        {
            throw new ArgumentException("Microsoft 365 mailbox user identifier is required.", nameof(mailboxUserId));
        }

        if (string.IsNullOrWhiteSpace(mailFolderId))
        {
            throw new ArgumentException("Microsoft 365 mail folder identifier is required.", nameof(mailFolderId));
        }

        var graphBaseUri = ValidateGraphBaseUrl(graphBaseUrl);
        var normalizedBaseUri = new Uri($"{graphBaseUri.GetLeftPart(UriPartial.Authority)}/");
        var select = Uri.EscapeDataString("id,subject,body,webLink,createdDateTime,lastModifiedDateTime,receivedDateTime");
        var filter = Uri.EscapeDataString($"receivedDateTime ge {receivedSince.UtcDateTime:O}");
        return new Uri(
            normalizedBaseUri,
            $"v1.0/users/{Uri.EscapeDataString(mailboxUserId)}/mailFolders/{Uri.EscapeDataString(mailFolderId)}/messages/delta?$select={select}&$filter={filter}");
    }

    private static Uri CreateStoredDeltaUri(string graphBaseUrl, string deltaLink)
    {
        var graphBaseUri = ValidateGraphBaseUrl(graphBaseUrl);
        if (!Uri.TryCreate(deltaLink, UriKind.Absolute, out var deltaUri)
            || deltaUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(deltaUri.Host, graphBaseUri.Host, StringComparison.OrdinalIgnoreCase)
            || deltaUri.Port != graphBaseUri.Port)
        {
            throw new ArgumentException(
                "Microsoft Graph delta link must be an HTTPS URL from the configured Graph authority.",
                nameof(deltaLink));
        }

        return deltaUri;
    }

    private static Uri ValidateGraphBaseUrl(string graphBaseUrl)
    {
        if (!Uri.TryCreate(graphBaseUrl, UriKind.Absolute, out var graphBaseUri)
            || graphBaseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Microsoft Graph base URL must use HTTPS.", nameof(graphBaseUrl));
        }

        return graphBaseUri;
    }

    private static MicrosoftOutlookMessageDelta MapMessage(Message message)
    {
        if (string.IsNullOrWhiteSpace(message.Id))
        {
            throw new MicrosoftExternalException(
                "Microsoft Graph Outlook message delta response contained an invalid message.");
        }

        var isDeleted = message.Removed is { ValueKind: JsonValueKind.Object };
        if (!isDeleted && message.Body?.Content is null)
        {
            throw new MicrosoftExternalException(
                "Microsoft Graph Outlook message delta response contained an incomplete active message.");
        }

        return new MicrosoftOutlookMessageDelta(
            message.Id,
            isDeleted ? null : message.Subject,
            isDeleted ? null : message.Body?.Content,
            isDeleted ? null : message.WebLink,
            message.CreatedDateTime,
            message.LastModifiedDateTime,
            message.ReceivedDateTime,
            isDeleted);
    }

    private sealed record Message(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("subject")] string? Subject,
        [property: JsonPropertyName("body")] MessageBody? Body,
        [property: JsonPropertyName("webLink")] string? WebLink,
        [property: JsonPropertyName("createdDateTime")] DateTimeOffset? CreatedDateTime,
        [property: JsonPropertyName("lastModifiedDateTime")] DateTimeOffset? LastModifiedDateTime,
        [property: JsonPropertyName("receivedDateTime")] DateTimeOffset? ReceivedDateTime,
        [property: JsonPropertyName("@removed")] JsonElement? Removed);

    private sealed record MessageBody(
        [property: JsonPropertyName("content")] string? Content);
}
