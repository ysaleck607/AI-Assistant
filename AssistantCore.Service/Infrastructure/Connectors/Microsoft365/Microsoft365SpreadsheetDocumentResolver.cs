using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Models.Messages.Connectors;
using AssistantCore.Service.Application.Models.Messages.Connectors.Microsoft365;
using AssistantCore.Service.Application.Models.Messages.Tabular;
using AssistantCore.Service.Application.Services.Messages.Connectors.Microsoft365;
using AssistantCore.Service.Application.Services.Messages.Tabular;

namespace AssistantCore.Service.Infrastructure.Connectors.Microsoft365;

public sealed class Microsoft365SpreadsheetDocumentResolver(
    IMicrosoft365IndexedContentRepository indexedContentRepository,
    IMicrosoft365UserGroupResolver groupResolver,
    IMicrosoft365SharePointGroupResolver sharePointGroupResolver,
    IMicrosoft365SearchAccessVerifier accessVerifier) : IMicrosoft365SpreadsheetDocumentResolver
{
    public async Task<Microsoft365SpreadsheetDocument> ResolveAsync(
        string fileName,
        ConnectorExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(context);
        EnsureMicrosoftIdentity(context);

        var requestedTitle = Path.GetFileNameWithoutExtension(fileName);
        var indexedCandidates = await indexedContentRepository.FindAvailableByTitleAsync(
            context.OrganizationId,
            requestedTitle,
            cancellationToken);

        if (indexedCandidates.Count == 0)
        {
            throw new InvalidOperationException($"Spreadsheet '{fileName}' was not found or is not accessible.");
        }

        var candidateRecords = indexedCandidates
            .Where(content =>
                !string.IsNullOrWhiteSpace(content.Microsoft365Source?.ExternalResourceId)
                && !string.IsNullOrWhiteSpace(content.Microsoft365Source.ParentExternalResourceId)
                && !string.IsNullOrWhiteSpace(content.ExternalContentId))
            .Select(content => new Microsoft365SearchRecord(
                "Microsoft365",
                content.Title ?? requestedTitle,
                string.Empty,
                content.Passages.FirstOrDefault()?.ChunkId ?? content.ExternalContentId,
                content.Microsoft365Source.ParentExternalResourceId,
                content.Microsoft365Source.ExternalResourceId,
                content.ExternalContentId,
                content.WebUrl,
                content.LastModifiedAt,
                RelevanceScore: null,
                SemanticScore: null))
            .ToArray();

        if (candidateRecords.Length == 0)
        {
            throw new InvalidOperationException($"Spreadsheet '{fileName}' was found but its Microsoft 365 document identifiers are incomplete.");
        }

        var normalizedUserId = context.EntraUserId!.Value.ToString("D");
        var entraGroupsTask = groupResolver.ResolveGroupIdsAsync(
            context.ExternalTenantId!,
            normalizedUserId,
            cancellationToken);
        var sharePointGroupsTask = sharePointGroupResolver.ResolveGroupIdsAsync(
            context.OrganizationId,
            context.ExternalTenantId!,
            normalizedUserId,
            cancellationToken);
        await Task.WhenAll(entraGroupsTask, sharePointGroupsTask);
        var entraGroups = await entraGroupsTask;
        var sharePointGroups = await sharePointGroupsTask;

        var authorizedRecords = await accessVerifier.KeepAuthorizedAsync(
            context.OrganizationId,
            context.ExternalTenantId!,
            normalizedUserId,
            entraGroups,
            sharePointGroups,
            candidateRecords,
            cancellationToken);

        if (authorizedRecords.Count == 0)
        {
            throw new InvalidOperationException($"Spreadsheet '{fileName}' was not found or is not accessible.");
        }

        if (authorizedRecords.Count > 1)
        {
            throw new InvalidOperationException(
                $"Several accessible spreadsheets are named '{fileName}'; use a unique file name.");
        }

        var record = authorizedRecords.Single();
        return new Microsoft365SpreadsheetDocument(
            fileName,
            context.ExternalTenantId!,
            record.DriveId!,
            record.DriveItemId!,
            record.Reference,
            record.Url);
    }

    private static void EnsureMicrosoftIdentity(ConnectorExecutionContext context)
    {
        if (context.OrganizationId == Guid.Empty
            || context.MemberId == Guid.Empty
            || context.IdentityProvider != IdentityProvider.MicrosoftEntraId
            || string.IsNullOrWhiteSpace(context.ExternalTenantId)
            || context.EntraUserId is null
            || context.EntraUserId == Guid.Empty
            || string.IsNullOrWhiteSpace(context.UserEmail))
        {
            throw new InvalidOperationException(
                "The authenticated member cannot be resolved to a Microsoft Entra identity.");
        }
    }
}
