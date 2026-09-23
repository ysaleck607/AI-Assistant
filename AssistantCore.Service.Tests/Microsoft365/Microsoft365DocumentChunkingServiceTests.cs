using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Models.Microsoft365.ContentExtraction;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365DocumentChunkingServiceTests
{
    [Theory, AutoDomainData]
    public void Given_TheSameDocument_When_CreateChunks_Then_ProducesDeterministicPassages(
        Guid organizationId,
        Guid sourceId,
        string siteId,
        string driveId,
        string itemId,
        string version,
        string title,
        string url)
    {
        // Given
        var service = CreateService(maximumTokens: 12, overlapTokens: 2);
        var units = new[]
        {
            new Microsoft365ExtractedContentUnit(
                Microsoft365ExtractedContentUnitKind.Paragraph,
                0,
                string.Join(' ', Enumerable.Repeat("contenu", 30)),
                "word/document.xml")
        };

        // When
        var first = service.CreateChunks(
            organizationId, sourceId, siteId, driveId, itemId, version, title, url, null, units);
        var second = service.CreateChunks(
            organizationId, sourceId, siteId, driveId, itemId, version, title, url, null, units);

        // Then
        Assert.Equal(first, second);
        Assert.True(first.Count > 1);
        Assert.All(first, passage =>
        {
            Assert.Equal(itemId, passage.DriveItemId);
            Assert.True(passage.Content.Length <= 48);
        });
    }

    [Theory, AutoDomainData]
    public void Given_AnEmptyDocument_When_CreateChunks_Then_ReturnsNoPassages(
        Guid organizationId,
        Guid sourceId,
        string siteId,
        string driveId,
        string itemId,
        string version,
        string title)
    {
        // Given
        var service = CreateService(800, 100);

        // When
        var passages = service.CreateChunks(
            organizationId,
            sourceId,
            siteId,
            driveId,
            itemId,
            version,
            title,
            null,
            null,
            []);

        // Then
        Assert.Empty(passages);
    }

    [Theory, AutoDomainData]
    public void Given_ASectionSpanningSeveralChunks_When_CreateChunks_Then_RepeatsItsContext(
        Guid organizationId,
        Guid sourceId,
        string siteId,
        string driveId,
        string itemId,
        string version,
        string title)
    {
        // Given
        var service = CreateService(maximumTokens: 20, overlapTokens: 2);
        var units = new[]
        {
            new Microsoft365ExtractedContentUnit(
                Microsoft365ExtractedContentUnitKind.Header,
                0,
                "Accès au bâtiment",
                "word/document.xml"),
            new Microsoft365ExtractedContentUnit(
                Microsoft365ExtractedContentUnitKind.Paragraph,
                1,
                string.Join(' ', Enumerable.Repeat("procédure", 40)),
                "word/document.xml")
        };

        // When
        var passages = service.CreateChunks(
            organizationId,
            sourceId,
            siteId,
            driveId,
            itemId,
            version,
            title,
            null,
            null,
            units);

        // Then
        Assert.True(passages.Count > 1);
        Assert.All(passages.Skip(1), passage =>
            Assert.StartsWith("Section: Accès au bâtiment", passage.Content, StringComparison.Ordinal));
    }

    [Theory, AutoDomainData]
    public void Given_ArchiveGroupsExceedingTheDocumentLimit_When_CreateChunks_Then_ThrowsInvalidDataException(
        Guid organizationId,
        Guid sourceId,
        string siteId,
        string driveId,
        string itemId,
        string version,
        string title)
    {
        // Given
        var service = CreateService(maximumTokens: 5, overlapTokens: 0, maximumChunksPerDocument: 2);
        var units = new[]
        {
            new Microsoft365ExtractedContentUnit(
                Microsoft365ExtractedContentUnitKind.Paragraph,
                0,
                new string('a', 30),
                "first/document.xml"),
            new Microsoft365ExtractedContentUnit(
                Microsoft365ExtractedContentUnitKind.Paragraph,
                1,
                new string('b', 30),
                "second/document.xml")
        };

        // When
        var action = () => service.CreateChunks(
            organizationId,
            sourceId,
            siteId,
            driveId,
            itemId,
            version,
            title,
            null,
            null,
            units);

        // Then
        var exception = Assert.Throws<InvalidDataException>(action);
        Assert.Contains("2 passages", exception.Message, StringComparison.Ordinal);
    }

    private static Microsoft365DocumentChunkingService CreateService(
        int maximumTokens,
        int overlapTokens,
        int maximumChunksPerDocument = 100) =>
        new(Options.Create(new Microsoft365Options
        {
            ChunkMaximumTokens = maximumTokens,
            ChunkOverlapTokens = overlapTokens,
            MaximumChunksPerDocument = maximumChunksPerDocument
        }));
}
