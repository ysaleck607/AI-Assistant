using System.Text.Json.Serialization;
using AssistantCore.ExternalServices.Entities.Microsoft;

namespace AssistantCore.ExternalServices.Services.Microsoft;

public sealed class MicrosoftGraphOutlookMailboxQueryClient(HttpClient httpClient)
{
    private const int MaximumCandidateCount = 100;
    private const string MessageSelect =
        "id,subject,bodyPreview,webLink,receivedDateTime,sentDateTime,from";
    private static readonly string[] SupportedScopes = ["received", "sent", "all"];
    private readonly MicrosoftGraphCollectionReader collectionReader = new(httpClient);
    private readonly MicrosoftGraphOutlookMessageBodyBatchReader bodyReader = new(httpClient);

    public async Task<IReadOnlyCollection<MicrosoftOutlookMailboxMessage>> QueryAsync(
        string graphBaseUrl,
        string accessToken,
        string mailboxUserId,
        string? query,
        string? sender,
        string? recipient,
        string scope,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        bool includeBody,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateArguments(graphBaseUrl, accessToken, mailboxUserId, scope, dateFrom, dateTo, limit);

        var hasSearch = !string.IsNullOrWhiteSpace(query)
            || !string.IsNullOrWhiteSpace(sender)
            || !string.IsNullOrWhiteSpace(recipient);
        var candidateLimit = hasSearch ? MaximumCandidateCount : limit;
        var uri = CreateQueryUri(
            graphBaseUrl,
            mailboxUserId,
            query,
            sender,
            recipient,
            scope,
            dateFrom,
            dateTo,
            candidateLimit);
        var messages = hasSearch
            ? await collectionReader.ReadAsync<Message, MicrosoftOutlookMailboxMessage>(
                uri,
                accessToken,
                MapMessage,
                "Outlook mailbox messages",
                cancellationToken)
            : await collectionReader.ReadFirstPageAsync<Message, MicrosoftOutlookMailboxMessage>(
                uri,
                accessToken,
                MapMessage,
                "Outlook mailbox messages",
                cancellationToken);

        var selectedMessages = messages
            .Where(message => IsWithinDateRange(message, scope, dateFrom, dateTo))
            .OrderByDescending(message => GetRelevantDate(message, scope))
            .Take(limit)
            .ToArray();
        if (!includeBody || selectedMessages.Length == 0)
        {
            return selectedMessages;
        }

        var bodies = await bodyReader.ReadAsync(
            graphBaseUrl,
            accessToken,
            mailboxUserId,
            selectedMessages.Select(message => message.Id).ToArray(),
            cancellationToken);
        return selectedMessages
            .Select(message => message with { BodyContent = bodies[message.Id] })
            .ToArray();
    }

    private static Uri CreateQueryUri(
        string graphBaseUrl,
        string mailboxUserId,
        string? query,
        string? sender,
        string? recipient,
        string scope,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        int top)
    {
        var graphBaseUri = new Uri(graphBaseUrl);
        var normalizedBaseUri = new Uri($"{graphBaseUri.GetLeftPart(UriPartial.Authority)}/");
        var collectionPath = scope switch
        {
            "received" => "mailFolders/inbox/messages",
            "sent" => "mailFolders/sentitems/messages",
            _ => "messages"
        };
        var parameters = new List<string>
        {
            $"$select={Uri.EscapeDataString(MessageSelect)}",
            $"$top={top}"
        };

        var searchExpression = CreateSearchExpression(query, sender, recipient);
        if (searchExpression is not null)
        {
            parameters.Add($"$search={Uri.EscapeDataString(searchExpression)}");
        }
        else
        {
            var dateField = scope == "sent" ? "sentDateTime" : "receivedDateTime";
            var filter = CreateDateFilter(dateField, dateFrom, dateTo);
            if (filter is not null)
            {
                parameters.Add($"$filter={Uri.EscapeDataString(filter)}");
            }
            parameters.Add($"$orderby={Uri.EscapeDataString($"{dateField} desc")}");
        }

        return new Uri(
            normalizedBaseUri,
            $"v1.0/users/{Uri.EscapeDataString(mailboxUserId)}/{collectionPath}?{string.Join('&', parameters)}");
    }

    private static string? CreateSearchExpression(
        string? query,
        string? sender,
        string? recipient)
    {
        var clauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(sender))
        {
            clauses.Add($"from:{EscapeSearchValue(sender)}");
        }
        if (!string.IsNullOrWhiteSpace(recipient))
        {
            clauses.Add($"to:{EscapeSearchValue(recipient)}");
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            clauses.Add(EscapeSearchValue(query));
        }
        return clauses.Count == 0 ? null : $"\"{string.Join(" AND ", clauses)}\"";
    }

    private static string EscapeSearchValue(string value) =>
        value.Trim().Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string? CreateDateFilter(
        string field,
        DateOnly? dateFrom,
        DateOnly? dateTo)
    {
        var clauses = new List<string>();
        if (dateFrom is { } from)
        {
            clauses.Add($"{field} ge {from:yyyy-MM-dd}T00:00:00Z");
        }
        if (dateTo is { } to)
        {
            clauses.Add($"{field} lt {to.AddDays(1):yyyy-MM-dd}T00:00:00Z");
        }
        return clauses.Count == 0 ? null : string.Join(" and ", clauses);
    }

    private static bool IsWithinDateRange(
        MicrosoftOutlookMailboxMessage message,
        string scope,
        DateOnly? dateFrom,
        DateOnly? dateTo)
    {
        var value = GetRelevantDate(message, scope);
        if (value is null)
        {
            return dateFrom is null && dateTo is null;
        }

        var date = DateOnly.FromDateTime(value.Value.UtcDateTime);
        return (dateFrom is null || date >= dateFrom)
            && (dateTo is null || date <= dateTo);
    }

    private static DateTimeOffset? GetRelevantDate(
        MicrosoftOutlookMailboxMessage message,
        string scope) =>
        scope == "sent" ? message.SentAt : message.ReceivedAt ?? message.SentAt;

    private static MicrosoftOutlookMailboxMessage MapMessage(Message message)
    {
        if (string.IsNullOrWhiteSpace(message.Id))
        {
            throw new MicrosoftExternalException(
                "Microsoft Graph Outlook mailbox response contained an invalid message.");
        }

        return new MicrosoftOutlookMailboxMessage(
            message.Id,
            string.IsNullOrWhiteSpace(message.Subject) ? "Courriel Outlook" : message.Subject.Trim(),
            message.BodyPreview?.Trim() ?? string.Empty,
            BodyContent: null,
            SenderName: message.From?.EmailAddress?.Name?.Trim(),
            SenderAddress: message.From?.EmailAddress?.Address?.Trim(),
            ReceivedAt: message.ReceivedDateTime,
            SentAt: message.SentDateTime,
            WebLink: message.WebLink);
    }

    private static void ValidateArguments(
        string graphBaseUrl,
        string accessToken,
        string mailboxUserId,
        string scope,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        int limit)
    {
        if (!Uri.TryCreate(graphBaseUrl, UriKind.Absolute, out var graphBaseUri)
            || graphBaseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Microsoft Graph base URL must use HTTPS.", nameof(graphBaseUrl));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(mailboxUserId);
        if (!SupportedScopes.Contains(scope, StringComparer.Ordinal))
        {
            throw new ArgumentException("The Outlook mailbox scope is invalid.", nameof(scope));
        }
        if (dateFrom > dateTo)
        {
            throw new ArgumentException("The start date cannot be after the end date.", nameof(dateFrom));
        }
        if (limit is <= 0 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }
    }

    private sealed record Message(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("subject")] string? Subject,
        [property: JsonPropertyName("bodyPreview")] string? BodyPreview,
        [property: JsonPropertyName("webLink")] string? WebLink,
        [property: JsonPropertyName("receivedDateTime")] DateTimeOffset? ReceivedDateTime,
        [property: JsonPropertyName("sentDateTime")] DateTimeOffset? SentDateTime,
        [property: JsonPropertyName("from")] Recipient? From);

    private sealed record Recipient(
        [property: JsonPropertyName("emailAddress")] EmailAddress? EmailAddress);

    private sealed record EmailAddress(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("address")] string? Address);
}
