using System.Text.Json.Serialization;
using System.Net.Http.Json;
using AssistantCore.ExternalServices.Entities.Microsoft;

namespace AssistantCore.ExternalServices.Services.Microsoft;

public sealed class MicrosoftGraphOutlookAttachmentClient(HttpClient httpClient)
{
    private readonly MicrosoftGraphCollectionReader collectionReader = new(httpClient);

    public async Task<IReadOnlyCollection<MicrosoftOutlookFileAttachment>> GetFileAttachmentsAsync(
        string graphBaseUrl,
        string accessToken,
        string mailboxUserId,
        string messageId,
        long maximumBytes,
        CancellationToken cancellationToken = default)
    {
        var attachmentIds = await collectionReader.ReadAsync<AttachmentSummary, AttachmentSummary>(
            CreateCollectionUri(graphBaseUrl, mailboxUserId, messageId),
            accessToken,
            attachment => attachment,
            "Outlook message attachments",
            cancellationToken);

        var attachments = new List<MicrosoftOutlookFileAttachment>();
        foreach (var attachment in attachmentIds)
        {
            if (string.IsNullOrWhiteSpace(attachment.Id)
                || string.IsNullOrWhiteSpace(attachment.Name)
                || attachment.IsInline
                || (attachment.Size is > 0 && attachment.Size > maximumBytes)
                || !string.Equals(attachment.ODataType, "#microsoft.graph.fileAttachment", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var file = await ReadFileAttachmentAsync(
                graphBaseUrl,
                accessToken,
                mailboxUserId,
                messageId,
                attachment.Id,
                cancellationToken);
            if (file.ContentBytes is null)
            {
                continue;
            }

            byte[] content;
            try
            {
                content = Convert.FromBase64String(file.ContentBytes);
            }
            catch (FormatException)
            {
                continue;
            }
            if (content.Length > maximumBytes)
            {
                continue;
            }

            attachments.Add(new MicrosoftOutlookFileAttachment(
                attachment.Id,
                attachment.Name,
                attachment.ContentType,
                content,
                attachment.IsInline));
        }

        return attachments;
    }

    private async Task<FileAttachment> ReadFileAttachmentAsync(
        string graphBaseUrl,
        string accessToken,
        string mailboxUserId,
        string messageId,
        string attachmentId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            CreateAttachmentUri(graphBaseUrl, mailboxUserId, messageId, attachmentId));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var retryAfter = response.Headers.RetryAfter;
            throw new MicrosoftExternalException(
                $"Microsoft Graph Outlook attachment lookup failed with status {(int)response.StatusCode}.",
                statusCode: response.StatusCode,
                retryAfterDelay: retryAfter?.Delta,
                retryAfterAt: retryAfter?.Date);
        }

        return await response.Content.ReadFromJsonAsync<FileAttachment>(cancellationToken)
            ?? throw new MicrosoftExternalException("Microsoft Graph Outlook attachment response was empty.");
    }

    private static Uri CreateCollectionUri(
        string graphBaseUrl,
        string mailboxUserId,
        string messageId)
    {
        var baseUri = ValidateBaseUri(graphBaseUrl);
        return new Uri(
            new Uri($"{baseUri.GetLeftPart(UriPartial.Authority)}/"),
            $"v1.0/users/{Uri.EscapeDataString(mailboxUserId)}/messages/{Uri.EscapeDataString(messageId)}/attachments?$select=id,name,contentType,size,isInline");
    }

    private static Uri CreateAttachmentUri(
        string graphBaseUrl,
        string mailboxUserId,
        string messageId,
        string attachmentId)
    {
        var baseUri = ValidateBaseUri(graphBaseUrl);
        return new Uri(
            new Uri($"{baseUri.GetLeftPart(UriPartial.Authority)}/"),
            $"v1.0/users/{Uri.EscapeDataString(mailboxUserId)}/messages/{Uri.EscapeDataString(messageId)}/attachments/{Uri.EscapeDataString(attachmentId)}");
    }

    private static Uri ValidateBaseUri(string graphBaseUrl) =>
        Uri.TryCreate(graphBaseUrl, UriKind.Absolute, out var baseUri)
            && baseUri.Scheme == Uri.UriSchemeHttps
            ? baseUri
            : throw new ArgumentException("Microsoft Graph base URL must use HTTPS.", nameof(graphBaseUrl));

    private sealed record AttachmentSummary(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("contentType")] string? ContentType,
        [property: JsonPropertyName("size")] long? Size,
        [property: JsonPropertyName("isInline")] bool IsInline,
        [property: JsonPropertyName("@odata.type")] string? ODataType);

    private sealed record FileAttachment(
        [property: JsonPropertyName("contentBytes")] string? ContentBytes);
}
