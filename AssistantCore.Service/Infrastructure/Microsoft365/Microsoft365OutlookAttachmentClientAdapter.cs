using System.Net;
using AssistantCore.ExternalServices.Services.Microsoft;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Infrastructure.Microsoft365;

public sealed class Microsoft365OutlookAttachmentClientAdapter(
    MicrosoftIdentityClient identityClient,
    MicrosoftGraphOutlookAttachmentClient graphClient,
    IOptions<Microsoft365Options> options) : IMicrosoft365OutlookAttachmentClient
{
    public async Task<IReadOnlyCollection<Microsoft365OutlookFileAttachment>> GetFileAttachmentsAsync(
        string tenantId,
        string mailboxUserId,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        var configuration = options.Value;
        try
        {
            var token = await identityClient.AcquireApplicationTokenAsync(
                configuration.AuthorityBaseUrl,
                tenantId,
                configuration.ClientId,
                configuration.ClientSecret,
                cancellationToken);
            var attachments = await graphClient.GetFileAttachmentsAsync(
                configuration.GraphBaseUrl,
                token.AccessToken,
                mailboxUserId,
                messageId,
                configuration.MaximumExtractionFileSizeBytes,
                cancellationToken);
            return attachments
                .Select(attachment => new Microsoft365OutlookFileAttachment(
                    attachment.Id,
                    attachment.Name,
                    attachment.ContentType,
                    attachment.Content,
                    attachment.IsInline))
                .ToArray();
        }
        catch (MicrosoftExternalException exception)
        {
            throw exception.StatusCode == HttpStatusCode.Forbidden
                ? new Microsoft365SourceAccessDeniedException(
                    "Microsoft 365 Outlook attachment access was denied.", exception)
                : exception.StatusCode == HttpStatusCode.TooManyRequests
                    || (exception.StatusCode.HasValue && (int)exception.StatusCode.Value >= 500)
                    ? new Microsoft365GraphTransientException(
                        "Microsoft 365 Outlook attachment lookup failed temporarily.", exception)
                    : new Microsoft365ExternalException(
                        "Microsoft 365 Outlook attachments could not be loaded.", exception);
        }
    }
}
