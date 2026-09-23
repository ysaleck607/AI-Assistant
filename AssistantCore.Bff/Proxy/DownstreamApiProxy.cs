using AssistantCore.Bff.Configuration;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using System.Net.Http.Headers;

namespace AssistantCore.Bff.Proxy;

public sealed class DownstreamApiProxy(
    IHttpClientFactory httpClientFactory,
    ITokenAcquisition tokenAcquisition,
    IOptions<DownstreamApiOptions> options,
    IAntiforgery antiforgery)
{
    private static readonly HashSet<string> RequestHeadersToSkip = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Host",
        "Content-Length",
        "Connection",
        "Keep-Alive",
        "Proxy-Authorization",
        "Proxy-Authenticate",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade",
        "Forwarded",
        "X-Forwarded-For",
        "X-Forwarded-Host",
        "X-Forwarded-Proto",
        "X-Forwarded-Port",
        "X-Original-For",
        "X-Original-Host",
        "X-Original-Proto",
        "X-OnPremia-Edge-Client",
        "X-CSRF-TOKEN"
    };

    private static readonly HashSet<string> ResponseHeadersToSkip = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Keep-Alive",
        "Proxy-Authenticate",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade",
        // Browser session state belongs exclusively to the BFF. Never surface an
        // internal API cookie through the transparent proxy.
        "Set-Cookie"
    };

    private readonly DownstreamApiOptions downstreamApiOptions = options.Value;

    public async Task ProxyAsync(HttpContext context, string path, CancellationToken cancellationToken)
    {
        if (RequiresAntiforgeryValidation(context.Request.Method))
        {
            await antiforgery.ValidateRequestAsync(context);
        }

        var accessToken = await tokenAcquisition.GetAccessTokenForUserAsync(
            downstreamApiOptions.Scopes,
            user: context.User);

        using var request = CreateDownstreamRequest(context, path, accessToken);
        var httpClient = httpClientFactory.CreateClient(nameof(DownstreamApiProxy));
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        context.Response.StatusCode = (int)response.StatusCode;
        CopyResponseHeaders(response, context.Response);

        if (response.Content is null)
        {
            return;
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        if (IsEventStream(response))
        {
            await CopyEventStreamAsync(responseStream, context.Response, cancellationToken);
            return;
        }

        await responseStream.CopyToAsync(context.Response.Body, cancellationToken);
    }

    private HttpRequestMessage CreateDownstreamRequest(
        HttpContext context,
        string path,
        string accessToken)
    {
        var downstreamUri = BuildDownstreamUri(path, context.Request.QueryString);
        var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), downstreamUri);

        if (CanHaveBody(context.Request.Method))
        {
            request.Content = new StreamContent(context.Request.Body);
            if (!string.IsNullOrWhiteSpace(context.Request.ContentType))
            {
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType);
            }
        }

        foreach (var header in context.Request.Headers)
        {
            if (RequestHeadersToSkip.Contains(header.Key))
            {
                continue;
            }

            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) && request.Content is not null)
            {
                request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private Uri BuildDownstreamUri(string path, QueryString queryString)
    {
        var baseUrl = downstreamApiOptions.BaseUrl.TrimEnd('/');
        var normalizedPath = path.TrimStart('/');
        return new Uri($"{baseUrl}/api/{normalizedPath}{queryString}", UriKind.Absolute);
    }

    private static async Task CopyEventStreamAsync(
        Stream source,
        HttpResponse destination,
        CancellationToken cancellationToken)
    {
        destination.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        await destination.StartAsync(cancellationToken);

        var buffer = new byte[4096];
        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            await destination.Body.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            await destination.Body.FlushAsync(cancellationToken);
        }
    }

    private static bool IsEventStream(HttpResponseMessage response) =>
        string.Equals(
            response.Content.Headers.ContentType?.MediaType,
            "text/event-stream",
            StringComparison.OrdinalIgnoreCase);

    private static void CopyResponseHeaders(HttpResponseMessage source, HttpResponse destination)
    {
        foreach (var header in source.Headers)
        {
            if (!ResponseHeadersToSkip.Contains(header.Key))
            {
                destination.Headers[header.Key] = header.Value.ToArray();
            }
        }

        foreach (var header in source.Content.Headers)
        {
            if (!ResponseHeadersToSkip.Contains(header.Key))
            {
                destination.Headers[header.Key] = header.Value.ToArray();
            }
        }

        destination.Headers.Remove("content-length");
    }

    private static bool RequiresAntiforgeryValidation(string method) =>
        !HttpMethods.IsGet(method)
        && !HttpMethods.IsHead(method)
        && !HttpMethods.IsOptions(method)
        && !HttpMethods.IsTrace(method);

    private static bool CanHaveBody(string method) =>
        !HttpMethods.IsGet(method)
        && !HttpMethods.IsHead(method)
        && !HttpMethods.IsTrace(method);
}
