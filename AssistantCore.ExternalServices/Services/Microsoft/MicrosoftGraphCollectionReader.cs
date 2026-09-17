using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssistantCore.ExternalServices.Services.Microsoft;

internal sealed class MicrosoftGraphCollectionReader(HttpClient httpClient)
{
    private const int MaximumRetryCount = 3;
    private static readonly TimeSpan InitialServerErrorBackoff = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaximumServerErrorBackoff = TimeSpan.FromSeconds(2);

    public async Task<IReadOnlyCollection<TResult>> ReadAsync<TItem, TResult>(
        Uri firstPageUri,
        string accessToken,
        Func<TItem, TResult> map,
        string resourceName,
        CancellationToken cancellationToken)
    {
        var results = new List<TResult>();
        await foreach (var page in ReadPagesAsync(
                           firstPageUri,
                           accessToken,
                           map,
                           resourceName,
                           cancellationToken))
        {
            results.AddRange(page.Items);
        }

        return results;
    }

    public async Task<IReadOnlyCollection<TResult>> ReadFirstPageAsync<TItem, TResult>(
        Uri pageUri,
        string accessToken,
        Func<TItem, TResult> map,
        string resourceName,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? preferValues = null)
    {
        var page = await ReadPageAsync<TItem>(
            pageUri,
            accessToken,
            resourceName,
            cancellationToken,
            preferValues);
        return page.Value.Select(map).ToArray();
    }

    public async IAsyncEnumerable<MicrosoftGraphCollectionPage<TResult>> ReadPagesAsync<TItem, TResult>(
        Uri firstPageUri,
        string accessToken,
        Func<TItem, TResult> map,
        string resourceName,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        IReadOnlyCollection<string>? preferValues = null)
    {
        var visitedPageUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Uri? pageUri = firstPageUri;

        while (pageUri is not null)
        {
            if (!visitedPageUris.Add(pageUri.AbsoluteUri))
            {
                throw new MicrosoftExternalException(
                    $"Microsoft Graph {resourceName} pagination contained a loop.");
            }

            var page = await ReadPageAsync<TItem>(
                pageUri,
                accessToken,
                resourceName,
                cancellationToken,
                preferValues);

            var deltaLink = ValidateOpaqueContinuationLink(
                firstPageUri,
                page.DeltaLink,
                resourceName);
            yield return new MicrosoftGraphCollectionPage<TResult>(
                page.Value.Select(map).ToArray(),
                deltaLink);
            pageUri = ResolveNextPageUri(firstPageUri, page.NextLink, resourceName);
        }
    }

    private async Task<GraphCollectionPage<TItem>> ReadPageAsync<TItem>(
        Uri pageUri,
        string accessToken,
        string resourceName,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? preferValues)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            using var request = new HttpRequestMessage(HttpMethod.Get, pageUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            if (preferValues is { Count: > 0 })
            {
                request.Headers.TryAddWithoutValidation("Prefer", string.Join(", ", preferValues));
            }

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var retryAfter = response.Headers.RetryAfter;
                var errorCode = await ReadGraphErrorCodeAsync(response, cancellationToken);
                if (ShouldRetry(response.StatusCode, attempt))
                {
                    await Task.Delay(
                        GetRetryDelay(response.StatusCode, retryAfter, attempt),
                        cancellationToken);
                    continue;
                }

                throw new MicrosoftExternalException(
                    $"Microsoft Graph {resourceName} lookup failed with status {(int)response.StatusCode}.",
                    statusCode: response.StatusCode,
                    errorCode: errorCode,
                    retryAfterDelay: retryAfter?.Delta,
                    retryAfterAt: retryAfter?.Date);
            }

            GraphCollectionResponse<TItem>? page;
            try
            {
                page = await response.Content.ReadFromJsonAsync<GraphCollectionResponse<TItem>>(
                    cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new MicrosoftExternalException(
                    $"Microsoft Graph {resourceName} response was invalid.",
                    exception);
            }

            if (page?.Value is null)
            {
                throw new MicrosoftExternalException(
                    $"Microsoft Graph {resourceName} response was empty.");
            }

            return new GraphCollectionPage<TItem>(page.Value, page.NextLink, page.DeltaLink);
        }
    }

    private static bool ShouldRetry(
        System.Net.HttpStatusCode statusCode,
        int attempt) =>
        attempt <= MaximumRetryCount
        && (statusCode == System.Net.HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500);

    private static TimeSpan GetRetryDelay(
        System.Net.HttpStatusCode statusCode,
        RetryConditionHeaderValue? retryAfter,
        int attempt)
    {
        if (statusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            return GetRetryAfterDelay(retryAfter) ?? GetBoundedServerErrorBackoff(attempt);
        }

        return GetBoundedServerErrorBackoff(attempt);
    }

    private static TimeSpan? GetRetryAfterDelay(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (retryAfter?.Date is { } retryAt)
        {
            var delay = retryAt - DateTimeOffset.UtcNow;
            return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }

        return null;
    }

    private static TimeSpan GetBoundedServerErrorBackoff(int attempt)
    {
        var multiplier = Math.Pow(2, Math.Max(0, attempt - 1));
        var milliseconds = Math.Min(
            InitialServerErrorBackoff.TotalMilliseconds * multiplier,
            MaximumServerErrorBackoff.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static async Task<string?> ReadGraphErrorCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<GraphErrorResponse>(
                cancellationToken);
            return error?.Error?.Code;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static Uri? ResolveNextPageUri(
        Uri firstPageUri,
        string? nextLink,
        string resourceName)
    {
        if (string.IsNullOrWhiteSpace(nextLink))
        {
            return null;
        }

        if (!Uri.TryCreate(nextLink, UriKind.Absolute, out var nextPageUri)
            || nextPageUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(nextPageUri.Host, firstPageUri.Host, StringComparison.OrdinalIgnoreCase)
            || nextPageUri.Port != firstPageUri.Port)
        {
            throw new MicrosoftExternalException(
                $"Microsoft Graph {resourceName} pagination URL was not trusted.");
        }

        return nextPageUri;
    }

    private static string? ValidateOpaqueContinuationLink(
        Uri firstPageUri,
        string? continuationLink,
        string resourceName)
    {
        if (string.IsNullOrWhiteSpace(continuationLink))
        {
            return null;
        }

        if (!Uri.TryCreate(continuationLink, UriKind.Absolute, out var continuationUri)
            || continuationUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(
                continuationUri.Host,
                firstPageUri.Host,
                StringComparison.OrdinalIgnoreCase)
            || continuationUri.Port != firstPageUri.Port)
        {
            throw new MicrosoftExternalException(
                $"Microsoft Graph {resourceName} continuation URL was not trusted.");
        }

        return continuationLink;
    }

    private sealed record GraphCollectionResponse<TItem>(
        [property: JsonPropertyName("value")] IReadOnlyCollection<TItem>? Value,
        [property: JsonPropertyName("@odata.nextLink")] string? NextLink,
        [property: JsonPropertyName("@odata.deltaLink")] string? DeltaLink);

    private sealed record GraphErrorResponse(
        [property: JsonPropertyName("error")] GraphError? Error);

    private sealed record GraphError(
        [property: JsonPropertyName("code")] string? Code);

    private sealed record GraphCollectionPage<TItem>(
        IReadOnlyCollection<TItem> Value,
        string? NextLink,
        string? DeltaLink);
}

internal sealed record MicrosoftGraphCollectionPage<TItem>(
    IReadOnlyCollection<TItem> Items,
    string? DeltaLink);
