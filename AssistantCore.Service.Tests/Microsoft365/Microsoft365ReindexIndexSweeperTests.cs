using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365ReindexIndexSweeperTests
{
    [Theory, AutoDomainData]
    public async Task Given_ADocumentMissingFromSharePoint_When_SweepAsync_Then_RemovesOnlyItsPassages(
        Guid operationId,
        Guid organizationId,
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        // Given
        var keptContent = CreateContent(organizationId, sourceId, "item-kept", "chunk-kept");
        var removedContent = CreateContent(organizationId, sourceId, "item-removed", "chunk-removed");
        var contentRepository = new StubReindexIndexedContentRepository([keptContent, removedContent]);
        var indexWriter = new StubReindexPassageIndexWriter();
        var sweeper = CreateSweeper(contentRepository, indexWriter, ["item-kept"]);

        // When
        var removedCount = await sweeper.SweepAsync(
            operationId,
            organizationId,
            sourceId,
            cancellationToken);

        // Then
        Assert.Equal(1, removedCount);
        Assert.Equal(["chunk-removed"], indexWriter.DeletedChunkIds);
        Assert.Equal([removedContent], contentRepository.DeletedContents);
    }

    [Theory, AutoDomainData]
    public async Task Given_EveryDocumentStillVisited_When_SweepAsync_Then_RemovesNothing(
        Guid operationId,
        Guid organizationId,
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        // Given
        var content = CreateContent(organizationId, sourceId, "item-kept", "chunk-kept");
        var contentRepository = new StubReindexIndexedContentRepository([content]);
        var indexWriter = new StubReindexPassageIndexWriter();
        var sweeper = CreateSweeper(contentRepository, indexWriter, ["item-kept"]);

        // When
        var removedCount = await sweeper.SweepAsync(
            operationId,
            organizationId,
            sourceId,
            cancellationToken);

        // Then
        Assert.Equal(0, removedCount);
        Assert.Empty(indexWriter.DeletedChunkIds);
        Assert.Empty(contentRepository.DeletedContents);
    }

    [Theory, AutoDomainData]
    public async Task Given_ADocumentStillQueuedForProcessing_When_SweepAsync_Then_KeepsIt(
        Guid operationId,
        Guid organizationId,
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        // Given : le document a produit un travail pendant la reprise, meme s'il n'est pas
        // encore traite. Il existe donc toujours dans SharePoint.
        var queuedContent = CreateContent(organizationId, sourceId, "item-queued", "chunk-queued");
        var contentRepository = new StubReindexIndexedContentRepository([queuedContent]);
        var indexWriter = new StubReindexPassageIndexWriter();
        var sweeper = CreateSweeper(contentRepository, indexWriter, ["item-queued"]);

        // When
        await sweeper.SweepAsync(operationId, organizationId, sourceId, cancellationToken);

        // Then
        Assert.Empty(contentRepository.DeletedContents);
    }

    private static Microsoft365ReindexIndexSweeper CreateSweeper(
        StubReindexIndexedContentRepository contentRepository,
        StubReindexPassageIndexWriter indexWriter,
        IReadOnlyCollection<string> visitedDocumentIds) =>
        new(
            new StubReindexOperationRepository { VisitedDocumentIds = visitedDocumentIds },
            contentRepository,
            indexWriter,
            NullLogger<Microsoft365ReindexIndexSweeper>.Instance);

    private static Microsoft365IndexedContent CreateContent(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        params string[] chunkIds) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Microsoft365SourceId = sourceId,
            ExternalContentId = externalContentId,
            Passages = chunkIds.Select(chunkId => new Microsoft365IndexedPassage
            {
                Id = Guid.NewGuid(),
                ChunkId = chunkId
            }).ToList()
        };
}
