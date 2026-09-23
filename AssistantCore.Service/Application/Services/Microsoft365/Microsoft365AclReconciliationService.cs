using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Models.Microsoft365.Permissions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public sealed class Microsoft365AclReconciliationService(
    IMicrosoft365IndexedContentRepository repository,
    IMicrosoft365AclResolver aclResolver,
    IMicrosoft365ContentAclSynchronizationService aclSynchronizationService,
    IMicrosoft365SecurityIdentityNormalizer identityNormalizer,
    IOptions<Microsoft365Options> options,
    TimeProvider timeProvider,
    ILogger<Microsoft365AclReconciliationService> logger)
    : IMicrosoft365AclReconciliationService
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<Microsoft365IndexedContent> candidates;
        do
        {
            candidates = await repository.GetAclReconciliationCandidatesAsync(
                timeProvider.GetUtcNow(),
                options.Value.AclReconciliationBatchSize,
                cancellationToken);
            foreach (var content in candidates)
            {
                try
                {
                    var resolution = await ResolveAclAsync(content, cancellationToken);
                    if (resolution is Microsoft365AclResolution.ResolvedAcl resolved)
                    {
                        await aclSynchronizationService.SynchronizeAsync(
                            content.OrganizationId,
                            content.Microsoft365SourceId,
                            content.ExternalContentId,
                            resolved.Acl,
                            cancellationToken);
                        continue;
                    }

                    await aclSynchronizationService.MarkUnavailableIfRegisteredAsync(
                        content.OrganizationId,
                        content.Microsoft365SourceId,
                        content.ExternalContentId,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    await HideAfterFailureAsync(content, cancellationToken);
                    logger.LogError(
                        exception,
                        "Microsoft 365 ACL reconciliation failed. IndexedContentId: {IndexedContentId}; SourceId: {SourceId}.",
                        content.Id,
                        content.Microsoft365SourceId);
                }
            }
        }
        while (candidates.Count == options.Value.AclReconciliationBatchSize);
    }

    private Task<Microsoft365AclResolution> ResolveAclAsync(
        Microsoft365IndexedContent content,
        CancellationToken cancellationToken)
    {
        if (content.Microsoft365Source is Microsoft365Source
            {
                Kind: Microsoft365SourceKind.OutlookMailbox,
                ExternalResourceId: var mailboxUserId
            })
        {
            if (string.IsNullOrWhiteSpace(mailboxUserId))
            {
                return Task.FromResult<Microsoft365AclResolution>(
                    new Microsoft365AclResolution.Unresolved(
                        Microsoft365AclResolutionFailureReason.UnknownPrincipal));
            }

            var acl = new Microsoft365Acl(
                [identityNormalizer.NormalizeEntraUserId(mailboxUserId)],
                [],
                [],
                hasAnonymousLink: false,
                hasOrganizationLink: false,
                Microsoft365AclInheritance.Unique);
            return Task.FromResult<Microsoft365AclResolution>(
                new Microsoft365AclResolution.ResolvedAcl(acl));
        }

        return ResolveSharePointAclAsync(content, cancellationToken);
    }

    private async Task<Microsoft365AclResolution> ResolveSharePointAclAsync(
        Microsoft365IndexedContent content,
        CancellationToken cancellationToken)
    {
        var contentReference = CreateContentReference(content);
        return await aclResolver.ResolveAsync(
            content.Organization,
            contentReference,
            cancellationToken);
    }

    private static Microsoft365ContentReference CreateContentReference(
        Microsoft365IndexedContent content) => content.Microsoft365Source switch
        {
            Microsoft365Drive drive => new Microsoft365ContentReference(
                Microsoft365ContentReferenceKind.DriveItem,
                drive.SiteId,
                drive.DriveId,
                ListId: null,
                content.ExternalContentId),
            Microsoft365List list => new Microsoft365ContentReference(
                Microsoft365ContentReferenceKind.ListItem,
                list.SiteId,
                DriveId: null,
                list.ListId,
                content.ExternalContentId,
                content.SiteUrl),
            _ => throw new InvalidOperationException(
                "The indexed content source does not support ACL reconciliation.")
        };

    private async Task HideAfterFailureAsync(
        Microsoft365IndexedContent content,
        CancellationToken cancellationToken)
    {
        try
        {
            await aclSynchronizationService.MarkUnavailableIfRegisteredAsync(
                content.OrganizationId,
                content.Microsoft365SourceId,
                content.ExternalContentId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception hideException)
        {
            logger.LogError(
                hideException,
                "Microsoft 365 content could not be hidden after ACL reconciliation failed. IndexedContentId: {IndexedContentId}.",
                content.Id);
            throw new InvalidOperationException(
                "Microsoft 365 content could not be hidden after ACL reconciliation failed.",
                hideException);
        }
    }
}
