using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssistantCore.ExternalServices.Services.Microsoft;

internal sealed class MicrosoftGraphOutlookMessageBodyBatchReader(HttpClient httpClient)
{
    private const int MaximumBatchSize = 20;
    private const string TextBodyPreference = "outlook.body-content-type=\"text\"";

    public async Task<IReadOnlyDictionary<string, string>> ReadAsync(
        string graphBaseUrl,
        string accessToken,
        string mailboxUserId,
        IReadOnlyCollection<string> messageIds,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(mailboxUserId);
        ArgumentNullException.ThrowIfNull(messageIds);

        var distinctMessageIds = messageIds
            .Where(messageId => !string.IsNullOrWhiteSpace(messageId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (distinctMessageIds.Length != messageIds.Count || distinctMessageIds.Length > MaximumBatchSize)
        {
            throw new ArgumentException(
                $"Between 1 and {MaximumBatchSize} distinct Outlook message identifiers are required.",
                nameof(messageIds));
        }
        if (distinctMessageIds.Length == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var graphBaseUri = ValidateGraphBaseUrl(graphBaseUrl);
        var normalizedBaseUri = new Uri($"{graphBaseUri.GetLeftPart(UriPartial.Authority)}/");
        var requestIds = distinctMessageIds
            .Select((messageId, index) => new BatchRequest(
                index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "GET",
                $"/users/{Uri.EscapeDataString(mailboxUserId)}/messages/{Uri.EscapeDataString(messageId)}?$select=body",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Prefer"] = TextBodyPreference
                }))
            .ToArray();

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(normalizedBaseUri, "v1.0/$batch"))
        {
            Content = JsonContent.Create(new BatchRequestEnvelope(requestIds))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new MicrosoftExternalException(
                $"Microsoft Graph Outlook message body batch failed with status {(int)response.StatusCode}.",
                statusCode: response.StatusCode);
        }

        BatchResponseEnvelope? batch;
        try
        {
            batch = await response.Content.ReadFromJsonAsync<BatchResponseEnvelope>(cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new MicrosoftExternalException(
                "Microsoft Graph Outlook message body batch response was invalid.",
                exception);
        }
        catch (NotSupportedException exception)
        {
            throw new MicrosoftExternalException(
                "Microsoft Graph Outlook message body batch response was invalid.",
                exception);
        }

        if (batch?.Responses is null || batch.Responses.Count != requestIds.Length)
        {
            throw new MicrosoftExternalException(
                "Microsoft Graph Outlook message body batch response was incomplete.");
        }

        var responsesById = batch.Responses.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var bodies = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < distinctMessageIds.Length; index++)
        {
            var requestId = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!responsesById.TryGetValue(requestId, out var item)
                || item.Status is < 200 or >= 300
                || item.Body?.Body?.Content is null)
            {
                throw new MicrosoftExternalException(
                    "Microsoft Graph could not return the complete Outlook message body batch.");
            }

            bodies.Add(distinctMessageIds[index], item.Body.Body.Content.Trim());
        }

        return bodies;
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

    private sealed record BatchRequestEnvelope(
        [property: JsonPropertyName("requests")] IReadOnlyCollection<BatchRequest> Requests);

    private sealed record BatchRequest(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("method")] string Method,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("headers")] IReadOnlyDictionary<string, string> Headers);

    private sealed record BatchResponseEnvelope(
        [property: JsonPropertyName("responses")] IReadOnlyCollection<BatchResponse>? Responses);

    private sealed record BatchResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("status")] int Status,
        [property: JsonPropertyName("body")] MessageEnvelope? Body);

    private sealed record MessageEnvelope(
        [property: JsonPropertyName("body")] MessageBody? Body);

    private sealed record MessageBody(
        [property: JsonPropertyName("content")] string? Content);
}
