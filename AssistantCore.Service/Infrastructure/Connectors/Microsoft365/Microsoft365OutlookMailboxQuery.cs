using System.Globalization;
using AssistantCore.ExternalServices.Services.Microsoft;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Messages.Connectors;
using AssistantCore.Service.Application.Models.Messages.Connectors.Microsoft365;
using AssistantCore.Service.Application.Models.Messages.Evidence;
using AssistantCore.Service.Application.Models.Messages.Tools.Arguments;
using AssistantCore.Service.Application.Services.Messages.Connectors.Microsoft365;
using AssistantCore.Service.Application.Services.Messages.Evidence;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Infrastructure.Connectors.Microsoft365;

public sealed class Microsoft365OutlookMailboxQueryAdapter(
    IMicrosoft365ApplicationTokenClient tokenClient,
    MicrosoftGraphOutlookMailboxQueryClient graphClient,
    IOptions<Microsoft365Options> microsoft365Options,
    Microsoft365ConnectorOptions connectorOptions,
    IEvidenceNormalizer evidenceNormalizer) : IMicrosoft365OutlookMailboxQuery
{
    public async Task<ConnectorResult> QueryAsync(
        QueryOutlookMailboxToolArguments request,
        ConnectorExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        EnsureMicrosoftIdentity(context);

        try
        {
            var accessToken = await tokenClient.AcquireGraphTokenAsync(
                context.ExternalTenantId!,
                cancellationToken);
            var messages = await graphClient.QueryAsync(
                microsoft365Options.Value.GraphBaseUrl,
                accessToken,
                context.EntraUserId!.Value.ToString("D"),
                request.Query,
                request.Sender,
                request.Recipient,
                request.Scope.Trim().ToLowerInvariant(),
                request.DateFrom,
                request.DateTo,
                request.IncludeBody,
                request.Limit,
                cancellationToken);

            var evidence = evidenceNormalizer.Normalize(
                messages.Select(message => new EvidenceCandidate(
                    "Microsoft365",
                    $"Courriel : {message.Subject}",
                    CreateEvidenceContent(message),
                    $"outlook:{message.Id}",
                    message.WebLink,
                    request.Scope.Equals("sent", StringComparison.OrdinalIgnoreCase)
                        ? message.SentAt
                        : message.ReceivedAt ?? message.SentAt,
                    RelevanceScore: null)).ToArray(),
                new EvidenceNormalizationOptions(
                    connectorOptions.MaximumContentLength,
                    Math.Min(request.Limit, connectorOptions.MaximumResults)));
            return new ConnectorResult(evidence);
        }
        catch (MicrosoftExternalException exception)
        {
            throw new Microsoft365ExternalException(
                "The Outlook mailbox could not be queried.",
                exception);
        }
    }

    private static string CreateEvidenceContent(
        AssistantCore.ExternalServices.Entities.Microsoft.MicrosoftOutlookMailboxMessage message)
    {
        var sender = !string.IsNullOrWhiteSpace(message.SenderName)
            ? message.SenderName
            : message.SenderAddress ?? "Expéditeur inconnu";
        var address = !string.IsNullOrWhiteSpace(message.SenderAddress)
            && !string.Equals(sender, message.SenderAddress, StringComparison.OrdinalIgnoreCase)
                ? $" <{message.SenderAddress}>"
                : string.Empty;
        var receivedAt = message.ReceivedAt?.ToString("O", CultureInfo.InvariantCulture)
            ?? "inconnue";
        var sentAt = message.SentAt?.ToString("O", CultureInfo.InvariantCulture)
            ?? "inconnue";
        var hasFullBody = !string.IsNullOrWhiteSpace(message.BodyContent);
        var content = hasFullBody
            ? message.BodyContent
            : string.IsNullOrWhiteSpace(message.BodyPreview)
                ? "Aucun aperçu disponible."
                : message.BodyPreview;
        var contentLabel = hasFullBody ? "Contenu" : "Aperçu";

        return $"Expéditeur : {sender}{address}\nReçu le : {receivedAt}\nEnvoyé le : {sentAt}\n{contentLabel} : {content}";
    }

    private static void EnsureMicrosoftIdentity(ConnectorExecutionContext context)
    {
        if (context.OrganizationId == Guid.Empty
            || context.MemberId == Guid.Empty
            || context.IdentityProvider != IdentityProvider.MicrosoftEntraId
            || string.IsNullOrWhiteSpace(context.ExternalTenantId)
            || context.EntraUserId is null
            || context.EntraUserId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "The authenticated member cannot be resolved to a Microsoft Entra identity.");
        }
    }
}
