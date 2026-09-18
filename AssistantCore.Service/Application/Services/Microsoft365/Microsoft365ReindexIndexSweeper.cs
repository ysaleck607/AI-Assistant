using AssistantCore.Repository.Repositories;
using Microsoft.Extensions.Logging;

namespace AssistantCore.Service.Application.Services.Microsoft365;

/// <summary>
/// Applique les suppressions a la fin d'une reprise. Un document encore present dans
/// SharePoint a produit un travail pendant la reprise : il est donc conserve, meme si son
/// traitement n'est pas encore termine. Les autres n'existent plus et sortent de l'index.
/// </summary>
public sealed class Microsoft365ReindexIndexSweeper(
    IMicrosoft365ReindexOperationRepository reindexOperationRepository,
    IMicrosoft365IndexedContentRepository indexedContentRepository,
    IMicrosoft365PassageIndexWriter indexWriter,
    ILogger<Microsoft365ReindexIndexSweeper> logger) : IMicrosoft365ReindexIndexSweeper
{
    public async Task<int> SweepAsync(
        Guid operationId,
        Guid organizationId,
        Guid sourceId,
        CancellationToken cancellationToken = default)
    {
        var visitedDocumentIds = await reindexOperationRepository.GetVisitedDocumentIdsAsync(
            operationId,
            sourceId,
            cancellationToken);
        var visitedDocumentIdSet = visitedDocumentIds.ToHashSet(StringComparer.Ordinal);

        var contents = await indexedContentRepository.GetBySourceAsync(
            organizationId,
            sourceId,
            cancellationToken);
        var obsoleteContents = contents
            .Where(content => !visitedDocumentIdSet.Contains(content.ExternalContentId))
            .ToArray();
        if (obsoleteContents.Length == 0)
        {
            return 0;
        }

        var chunkIds = obsoleteContents
            .SelectMany(content => content.Passages)
            .Select(passage => passage.ChunkId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (chunkIds.Length > 0)
        {
            await indexWriter.DeleteAsync(organizationId, chunkIds, cancellationToken);
        }

        foreach (var content in obsoleteContents)
        {
            await indexedContentRepository.DeleteAsync(content, cancellationToken);
        }

        logger.LogInformation(
            "Microsoft 365 reindex removed content missing from SharePoint. OperationId={OperationId} "
            + "SourceId={SourceId} RemovedContentCount={RemovedContentCount} RemovedChunkCount={RemovedChunkCount}",
            operationId,
            sourceId,
            obsoleteContents.Length,
            chunkIds.Length);

        return obsoleteContents.Length;
    }
}
