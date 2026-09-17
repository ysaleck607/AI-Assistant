using System.Net;
using System.Runtime.CompilerServices;
using AssistantCore.ExternalServices.Services.Microsoft;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Infrastructure.Microsoft365;

public sealed class Microsoft365OutlookMessageDeltaClientAdapter(
    MicrosoftIdentityClient identityClient,
    MicrosoftGraphOutlookMessageDeltaClient graphClient,
    IOptions<Microsoft365Options> options) : IMicrosoft365OutlookMessageDeltaClient
{
    public IAsyncEnumerable<Microsoft365OutlookMessageDeltaPage> GetInitialPagesAsync(
        string tenantId,
        string mailboxUserId,
        string mailFolderId,
        DateTimeOffset receivedSince,
        CancellationToken cancellationToken = default) =>
        ReadPagesAsync(
            tenantId,
            (configuration, accessToken) => graphClient.GetInitialPagesAsync(
                configuration.GraphBaseUrl,
                accessToken,
                mailboxUserId,
                mailFolderId,
                receivedSince,
                cancellationToken),
            cancellationToken);

    public IAsyncEnumerable<Microsoft365OutlookMessageDeltaPage> GetDeltaPagesAsync(
        string tenantId,
        string deltaLink,
        CancellationToken cancellationToken = default) =>
        ReadPagesAsync(
            tenantId,
            (configuration, accessToken) => graphClient.GetDeltaPagesAsync(
                configuration.GraphBaseUrl,
                accessToken,
                deltaLink,
                cancellationToken),
            cancellationToken);

    private async IAsyncEnumerable<Microsoft365OutlookMessageDeltaPage> ReadPagesAsync(
        string tenantId,
        Func<Microsoft365Options, string,
            IAsyncEnumerable<AssistantCore.ExternalServices.Entities.Microsoft.MicrosoftOutlookMessageDeltaPage>> createPages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        AssistantCore.ExternalServices.Entities.Microsoft.MicrosoftAuthorizationCodeToken token;
        try
        {
            token = await identityClient.AcquireApplicationTokenAsync(
                configuration.AuthorityBaseUrl,
                tenantId,
                configuration.ClientId,
                configuration.ClientSecret,
                cancellationToken);
        }
        catch (MicrosoftExternalException exception)
        {
            throw CreateApplicationException(exception);
        }

        await using var enumerator = createPages(configuration, token.AccessToken)
            .GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            bool hasPage;
            try
            {
                hasPage = await enumerator.MoveNextAsync();
            }
            catch (MicrosoftExternalException exception)
            {
                throw CreateApplicationException(exception);
            }

            if (!hasPage)
            {
                yield break;
            }

            var page = enumerator.Current;
            yield return new Microsoft365OutlookMessageDeltaPage(
                page.Items.Select(item => new Microsoft365OutlookMessageDelta(
                    item.Id,
                    item.Subject,
                    item.BodyContent,
                    item.WebLink,
                    item.CreatedDateTime,
                    item.LastModifiedDateTime,
                    item.ReceivedDateTime,
                    item.IsDeleted)).ToArray(),
                page.DeltaLink);
        }
    }

    private static Exception CreateApplicationException(MicrosoftExternalException exception) =>
        exception.StatusCode == HttpStatusCode.Forbidden
            ? new Microsoft365SourceAccessDeniedException(
                "Microsoft 365 Outlook source access was denied.", exception)
            : IsInvalidDeltaCheckpoint(exception)
                ? new Microsoft365DeltaCheckpointInvalidException(
                    "Microsoft 365 Outlook message delta checkpoint is invalid.", exception)
                : IsTransient(exception)
                    ? new Microsoft365GraphTransientException(
                        "Microsoft 365 Outlook message delta failed temporarily.", exception)
                    : new Microsoft365ExternalException(
                        "Microsoft 365 Outlook message delta could not be loaded.", exception);

    private static bool IsInvalidDeltaCheckpoint(MicrosoftExternalException exception) =>
        exception.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound
        || string.Equals(exception.ErrorCode, "syncStateNotFound", StringComparison.OrdinalIgnoreCase)
        || string.Equals(exception.ErrorCode, "resyncRequired", StringComparison.OrdinalIgnoreCase);

    private static bool IsTransient(MicrosoftExternalException exception) =>
        exception.StatusCode == HttpStatusCode.TooManyRequests
        || (exception.StatusCode.HasValue && (int)exception.StatusCode.Value >= 500);
}
