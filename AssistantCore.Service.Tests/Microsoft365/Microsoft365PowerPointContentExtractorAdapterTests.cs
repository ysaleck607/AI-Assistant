using AssistantCore.ExternalServices.Services.Microsoft;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Models.Microsoft365.ContentExtraction;
using AssistantCore.Service.Infrastructure.Microsoft365;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365PowerPointContentExtractorAdapterTests
{
    [Theory]
    [InlineAutoDomainData(".ppt")]
    [InlineAutoDomainData(".pptx")]
    [InlineAutoDomainData(".pptm")]
    public void Given_ASupportedExtension_When_CanExtract_Then_ReturnsTrue(string extension, string fileName)
    {
        // Given
        var adapter = CreateAdapter();

        // When
        var canExtract = adapter.CanExtract($"{fileName}{extension}", null);

        // Then
        Assert.True(canExtract);
    }

    [Theory, AutoDomainData]
    public void Given_AnUnsupportedExtension_When_CanExtract_Then_ReturnsFalse(string fileName)
    {
        // Given
        var adapter = CreateAdapter();

        // When
        var canExtract = adapter.CanExtract($"{fileName}.docx", null);

        // Then
        Assert.False(canExtract);
    }

    [Theory, AutoDomainData]
    public async Task Given_ALegacyPresentation_When_ExtractAsync_Then_MapsUnsupportedFormat(
        string fileName,
        byte[] content)
    {
        // Given
        var adapter = CreateAdapter();
        var request = new Microsoft365ContentExtractionRequest(
            $"{fileName}.ppt",
            null,
            new MemoryStream(content),
            content.Length);

        // When
        var result = await adapter.ExtractAsync(request, CancellationToken.None);

        // Then
        Assert.Equal(Microsoft365ContentExtractionStatus.UnsupportedFormat, result.Status);
    }

    [Theory, AutoDomainData]
    public async Task Given_ATitleSlide_When_ExtractAsync_Then_MapsToSuccessWithATitleUnitTaggedWithItsSlide(
        string fileName)
    {
        // Given
        var adapter = CreateAdapter();
        var slideXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <p:sld
                xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <p:cSld>
                <p:spTree>
                  <p:sp>
                    <p:nvSpPr><p:cNvPr id="2" name="Title"/><p:cNvSpPr/><p:nvPr><p:ph type="title"/></p:nvPr></p:nvSpPr>
                    <p:txBody><a:p><a:r><a:t>Bienvenue</a:t></a:r></a:p></p:txBody>
                  </p:sp>
                </p:spTree>
              </p:cSld>
            </p:sld>
            """;
        await using var package = CreatePackage(slideXml);
        var request = new Microsoft365ContentExtractionRequest($"{fileName}.pptx", null, package, package.Length);

        // When
        var result = await adapter.ExtractAsync(request, CancellationToken.None);

        // Then
        Assert.Equal(Microsoft365ContentExtractionStatus.Success, result.Status);
        var unit = Assert.Single(result.Units);
        Assert.Equal(Microsoft365ExtractedContentUnitKind.Title, unit.Kind);
        Assert.Equal("Diapositive 1 — Bienvenue", unit.Text);
        Assert.Equal("slide:1", unit.SourcePart);
    }

    private static Microsoft365PowerPointContentExtractorAdapter CreateAdapter() =>
        new(
            new MicrosoftPowerPointContentExtractorClient(),
            Options.Create(new Microsoft365Options()));

    private static System.IO.MemoryStream CreatePackage(string slideXml)
    {
        var stream = new System.IO.MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(
                   stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(
                archive,
                "ppt/presentation.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <p:presentation
                    xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                    xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <p:sldIdLst><p:sldId id="256" r:id="rId2"/></p:sldIdLst>
                </p:presentation>
                """);
            AddEntry(
                archive,
                "ppt/_rels/presentation.xml.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide1.xml"/>
                </Relationships>
                """);
            AddEntry(archive, "ppt/slides/slide1.xml", slideXml);
        }

        stream.Position = 0;
        return stream;
    }

    private static void AddEntry(System.IO.Compression.ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new System.IO.StreamWriter(entry.Open(), new System.Text.UTF8Encoding(false));
        writer.Write(content);
    }
}
