using System.IO.Compression;
using System.Text;
using AssistantCore.ExternalServices.Entities.Microsoft;
using AssistantCore.ExternalServices.Services.Microsoft;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class MicrosoftPowerPointContentExtractorClientTests
{
    private const long MaximumFileSize = 1_000_000;
    private const long MaximumExpandedSize = 2_000_000;
    private const int MaximumCharacters = 100_000;
    private const int MaximumSlides = 1_000;

    [Theory, AutoDomainData]
    public async Task Given_ATitleBulletsAndNotes_When_ExtractAsync_Then_ReproducesTheExpectedLayout(
        string fileName)
    {
        // Given
        var slide = Slide(
            titleShape: TitleShape("Résultats Q2"),
            bodyShapes:
            [
                BulletsShape(["Revenus en hausse de 8 %", "Marge stable"])
            ]);
        await using var package = CreatePackage(
            [slide],
            notesBySlideNumber: new Dictionary<int, string>
            {
                [1] = "Vérifier les chiffres avec Finance."
            });
        var client = new MicrosoftPowerPointContentExtractorClient();

        // When
        var result = await ExtractAsync(client, package, fileName);

        // Then
        Assert.Equal(MicrosoftPowerPointExtractionStatus.Success, result.Status);
        Assert.Equal(
            [
                "Diapositive 1 — Résultats Q2",
                "- Revenus en hausse de 8 %",
                "- Marge stable",
                "Notes : Vérifier les chiffres avec Finance."
            ],
            result.Units.Select(unit => unit.Text));
        Assert.All(result.Units, unit => Assert.Equal(1, unit.SlideNumber));
    }

    [Theory, AutoDomainData]
    public async Task Given_MultipleSlides_When_ExtractAsync_Then_NumbersThemInPresentationOrder(
        string fileName)
    {
        // Given
        var firstSlide = Slide(titleShape: TitleShape("Introduction"));
        var secondSlide = Slide(titleShape: TitleShape("Conclusion"));
        await using var package = CreatePackage([firstSlide, secondSlide]);
        var client = new MicrosoftPowerPointContentExtractorClient();

        // When
        var result = await ExtractAsync(client, package, fileName);

        // Then
        Assert.Equal(MicrosoftPowerPointExtractionStatus.Success, result.Status);
        Assert.Collection(
            result.Units,
            unit => Assert.Equal("Diapositive 1 — Introduction", unit.Text),
            unit => Assert.Equal("Diapositive 2 — Conclusion", unit.Text));
        Assert.Equal(1, result.Units[0].SlideNumber);
        Assert.Equal(2, result.Units[1].SlideNumber);
    }

    [Theory, AutoDomainData]
    public async Task Given_ShapesPositionedOutOfDocumentOrder_When_ExtractAsync_Then_OrdersThemByPosition(
        string fileName)
    {
        // Given
        var lowerShape = ParagraphShapeAt("Bas de la diapositive", x: 0, y: 500);
        var upperShape = ParagraphShapeAt("Haut de la diapositive", x: 0, y: 0);
        var slide = Slide(bodyShapes: [lowerShape, upperShape]);
        await using var firstPackage = CreatePackage([slide]);
        await using var secondPackage = CreatePackage([slide]);
        var client = new MicrosoftPowerPointContentExtractorClient();

        // When
        var first = await ExtractAsync(client, firstPackage, fileName);
        var second = await ExtractAsync(client, secondPackage, fileName);

        // Then
        Assert.Equal(MicrosoftPowerPointExtractionStatus.Success, first.Status);
        Assert.Equal(
            ["Haut de la diapositive", "Bas de la diapositive"],
            first.Units.Select(unit => unit.Text));
        Assert.Equal(first.Units, second.Units);
    }

    [Theory, AutoDomainData]
    public async Task Given_ATable_When_ExtractAsync_Then_ReturnsHeaderJoinedRows(
        string fileName)
    {
        // Given
        var slide = Slide(bodyShapes: [TableShape(["Jour", "Présence"], [["Mardi", "Oui"]])]);
        await using var package = CreatePackage([slide]);
        var client = new MicrosoftPowerPointContentExtractorClient();

        // When
        var result = await ExtractAsync(client, package, fileName);

        // Then
        Assert.Equal(MicrosoftPowerPointExtractionStatus.Success, result.Status);
        Assert.Equal(
            ["Tableau", "Jour : Mardi | Présence : Oui"],
            result.Units.Select(unit => unit.Text));
        Assert.All(result.Units, unit => Assert.Equal(MicrosoftPowerPointExtractedUnitKind.Table, unit.Kind));
    }

    [Theory, AutoDomainData]
    public async Task Given_AShapeWithNoTextButAlternativeText_When_ExtractAsync_Then_ExtractsTheAlternativeText(
        string fileName)
    {
        // Given
        var slide = Slide(bodyShapes: [PictureShapeWithAltText("Organigramme de l'équipe")]);
        await using var package = CreatePackage([slide]);
        var client = new MicrosoftPowerPointContentExtractorClient();

        // When
        var result = await ExtractAsync(client, package, fileName);

        // Then
        Assert.Equal(MicrosoftPowerPointExtractionStatus.Success, result.Status);
        Assert.Equal("Organigramme de l'équipe", Assert.Single(result.Units).Text);
    }

    [Theory, AutoDomainData]
    public async Task Given_AShapeWithNoTextAndNoAlternativeText_When_ExtractAsync_Then_IgnoresIt(
        string fileName)
    {
        // Given
        var slide = Slide(bodyShapes: [PictureShapeWithAltText(null)]);
        await using var package = CreatePackage([slide]);
        var client = new MicrosoftPowerPointContentExtractorClient();

        // When
        var result = await ExtractAsync(client, package, fileName);

        // Then
        Assert.Equal(MicrosoftPowerPointExtractionStatus.EmptyDocument, result.Status);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnEmptySlide_When_ExtractAsync_Then_ReturnsEmptyDocument(
        string fileName)
    {
        // Given
        await using var package = CreatePackage([Slide()]);
        var client = new MicrosoftPowerPointContentExtractorClient();

        // When
        var result = await ExtractAsync(client, package, fileName);

        // Then
        Assert.Equal(MicrosoftPowerPointExtractionStatus.EmptyDocument, result.Status);
        Assert.Empty(result.Units);
    }

    [Theory, AutoDomainData]
    public async Task Given_MacroExternalLinkAndEmbeddedObject_When_ExtractAsync_Then_ReturnsWarnings(
        string fileName)
    {
        // Given
        var slide = Slide(titleShape: TitleShape("Contenu"));
        await using var package = CreatePackage(
            [slide],
            additionalEntries: new Dictionary<string, string>
            {
                ["ppt/slides/_rels/slide1.xml.rels"] = """
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                      <Relationship Id="rId9" Type="hyperlink" Target="https://example.com" TargetMode="External" />
                    </Relationships>
                    """,
                ["ppt/vbaProject.bin"] = "macro-not-executed",
                ["ppt/embeddings/oleObject1.bin"] = "object-not-opened"
            });
        var client = new MicrosoftPowerPointContentExtractorClient();

        // When
        var result = await ExtractAsync(client, package, fileName, ".pptm");

        // Then
        Assert.Equal(MicrosoftPowerPointExtractionStatus.Success, result.Status);
        Assert.Equal(
            [
                MicrosoftPowerPointExtractionWarning.MacroIgnored,
                MicrosoftPowerPointExtractionWarning.ExternalLinkIgnored,
                MicrosoftPowerPointExtractionWarning.EmbeddedObjectIgnored
            ],
            result.Warnings);
    }

    [Theory, AutoDomainData]
    public async Task Given_AWrongExtension_When_ExtractAsync_Then_ReturnsUnsupportedFormat(
        string fileName)
    {
        // Given
        await using var package = CreatePackage([Slide(titleShape: TitleShape("Contenu"))]);
        var client = new MicrosoftPowerPointContentExtractorClient();

        // When
        var result = await ExtractAsync(client, package, fileName, ".ppt");

        // Then
        Assert.Equal(MicrosoftPowerPointExtractionStatus.UnsupportedFormat, result.Status);
    }

    [Theory, AutoDomainData]
    public async Task Given_AMismatchedMimeType_When_ExtractAsync_Then_ReturnsUnsupportedFormat(
        string fileName)
    {
        // Given
        await using var package = CreatePackage([Slide(titleShape: TitleShape("Contenu"))]);
        var client = new MicrosoftPowerPointContentExtractorClient();

        // When
        var result = await client.ExtractAsync(
            $"{fileName}.pptx",
            "application/vnd.ms-excel",
            package,
            package.Length,
            MaximumFileSize,
            MaximumExpandedSize,
            MaximumCharacters,
            MaximumSlides,
            CancellationToken.None);

        // Then
        Assert.Equal(MicrosoftPowerPointExtractionStatus.UnsupportedFormat, result.Status);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnEncryptedOfficeSignature_When_ExtractAsync_Then_ReturnsEncryptedDocument(
        string fileName)
    {
        // Given
        var signature = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
        await using var content = new MemoryStream(signature);
        var client = new MicrosoftPowerPointContentExtractorClient();

        // When
        var result = await ExtractAsync(client, content, fileName);

        // Then
        Assert.Equal(MicrosoftPowerPointExtractionStatus.EncryptedDocument, result.Status);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnInvalidPackage_When_ExtractAsync_Then_ReturnsCorruptedDocument(
        string fileName,
        byte[] invalidContent)
    {
        // Given
        await using var content = new MemoryStream([0x01, 0x02, 0x03, 0x04, .. invalidContent]);
        var client = new MicrosoftPowerPointContentExtractorClient();

        // When
        var result = await ExtractAsync(client, content, fileName);

        // Then
        Assert.Equal(MicrosoftPowerPointExtractionStatus.CorruptedDocument, result.Status);
    }

    [Theory, AutoDomainData]
    public async Task Given_APackageMissingThePresentationPart_When_ExtractAsync_Then_ReturnsCorruptedDocument(
        string fileName)
    {
        // Given
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "ppt/slides/slide1.xml", Slide(titleShape: TitleShape("Contenu")));
        }

        stream.Position = 0;
        await using var content = stream;
        var client = new MicrosoftPowerPointContentExtractorClient();

        // When
        var result = await ExtractAsync(client, content, fileName);

        // Then
        Assert.Equal(MicrosoftPowerPointExtractionStatus.CorruptedDocument, result.Status);
    }

    [Theory, AutoDomainData]
    public async Task Given_AFileAboveTheConfiguredLimit_When_ExtractAsync_Then_ReturnsTooLarge(
        string fileName)
    {
        // Given
        await using var package = CreatePackage([Slide(titleShape: TitleShape("Contenu"))]);
        var client = new MicrosoftPowerPointContentExtractorClient();

        // When
        var result = await client.ExtractAsync(
            $"{fileName}.pptx",
            null,
            package,
            MaximumFileSize + 1,
            MaximumFileSize,
            MaximumExpandedSize,
            MaximumCharacters,
            MaximumSlides,
            CancellationToken.None);

        // Then
        Assert.Equal(MicrosoftPowerPointExtractionStatus.TooLarge, result.Status);
    }

    [Theory, AutoDomainData]
    public async Task Given_MoreSlidesThanTheConfiguredLimit_When_ExtractAsync_Then_ReturnsTooLarge(
        string fileName)
    {
        // Given
        var slides = Enumerable.Range(1, 3)
            .Select(index => Slide(titleShape: TitleShape($"Diapositive {index}")))
            .ToArray();
        await using var package = CreatePackage(slides);
        var client = new MicrosoftPowerPointContentExtractorClient();

        // When
        var result = await client.ExtractAsync(
            $"{fileName}.pptx",
            null,
            package,
            package.Length,
            MaximumFileSize,
            MaximumExpandedSize,
            MaximumCharacters,
            maximumSlides: 2,
            CancellationToken.None);

        // Then
        Assert.Equal(MicrosoftPowerPointExtractionStatus.TooLarge, result.Status);
    }

    [Theory, AutoDomainData]
    public async Task Given_ContentAboveTheCharacterLimit_When_ExtractAsync_Then_ReturnsTooLarge(
        string fileName)
    {
        // Given
        await using var package = CreatePackage([Slide(titleShape: TitleShape(new string('a', 10)))]);
        var client = new MicrosoftPowerPointContentExtractorClient();

        // When
        var result = await client.ExtractAsync(
            $"{fileName}.pptx",
            null,
            package,
            package.Length,
            MaximumFileSize,
            MaximumExpandedSize,
            maximumExtractedCharacters: 5,
            MaximumSlides,
            CancellationToken.None);

        // Then
        Assert.Equal(MicrosoftPowerPointExtractionStatus.TooLarge, result.Status);
    }

    [Theory, AutoDomainData]
    public async Task Given_CancellationRequested_When_ExtractAsync_Then_ThrowsOperationCanceledException(
        string fileName)
    {
        // Given
        await using var package = CreatePackage([Slide(titleShape: TitleShape("Contenu"))]);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var client = new MicrosoftPowerPointContentExtractorClient();

        // When
        var exception = await Record.ExceptionAsync(() =>
            client.ExtractAsync(
                $"{fileName}.pptx",
                null,
                package,
                package.Length,
                MaximumFileSize,
                MaximumExpandedSize,
                MaximumCharacters,
                MaximumSlides,
                cancellation.Token));

        // Then
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    private static Task<MicrosoftPowerPointExtractionResult> ExtractAsync(
        MicrosoftPowerPointContentExtractorClient client,
        Stream content,
        string fileName,
        string extension = ".pptx") =>
        client.ExtractAsync(
            $"{fileName}{extension}",
            null,
            content,
            content.Length,
            MaximumFileSize,
            MaximumExpandedSize,
            MaximumCharacters,
            MaximumSlides,
            CancellationToken.None);

    private static MemoryStream CreatePackage(
        IReadOnlyList<string> slideXmls,
        IReadOnlyDictionary<int, string>? notesBySlideNumber = null,
        IReadOnlyDictionary<string, string>? additionalEntries = null)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "ppt/presentation.xml", PresentationXml(slideXmls.Count));
            AddEntry(archive, "ppt/_rels/presentation.xml.rels", PresentationRelationships(slideXmls.Count));

            for (var index = 0; index < slideXmls.Count; index++)
            {
                var slideNumber = index + 1;
                AddEntry(archive, $"ppt/slides/slide{slideNumber}.xml", slideXmls[index]);

                if (notesBySlideNumber is not null && notesBySlideNumber.TryGetValue(slideNumber, out var notesText))
                {
                    AddEntry(archive, $"ppt/notesSlides/notesSlide{slideNumber}.xml", NotesSlide(notesText));
                    AddEntry(
                        archive,
                        $"ppt/slides/_rels/slide{slideNumber}.xml.rels",
                        SlideNotesRelationship(slideNumber));
                }
            }

            foreach (var entry in additionalEntries ?? new Dictionary<string, string>())
            {
                AddEntry(archive, entry.Key, entry.Value);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static void AddEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string PresentationXml(int slideCount) => $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <p:presentation
            xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <p:sldIdLst>
            {string.Join('\n', Enumerable.Range(1, slideCount).Select(n => $"""<p:sldId id="{255 + n}" r:id="rId{n + 1}"/>"""))}
          </p:sldIdLst>
        </p:presentation>
        """;

    private static string PresentationRelationships(int slideCount) => $"""
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          {string.Join('\n', Enumerable.Range(1, slideCount).Select(n => $"""<Relationship Id="rId{n + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide{n}.xml"/>"""))}
        </Relationships>
        """;

    private static string SlideNotesRelationship(int slideNumber) => $"""
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/notesSlide" Target="../notesSlides/notesSlide{slideNumber}.xml"/>
        </Relationships>
        """;

    private static string NotesSlide(string text) => $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <p:notes
            xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
            xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <p:cSld>
            <p:spTree>
              <p:sp>
                <p:nvSpPr><p:cNvPr id="2" name="Notes"/><p:cNvSpPr/><p:nvPr><p:ph type="body"/></p:nvPr></p:nvSpPr>
                <p:txBody><a:p><a:r><a:t>{{text}}</a:t></a:r></a:p></p:txBody>
              </p:sp>
            </p:spTree>
          </p:cSld>
        </p:notes>
        """;

    private static string Slide(string? titleShape = null, IReadOnlyList<string>? bodyShapes = null) => $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <p:sld
            xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
            xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <p:cSld>
            <p:spTree>
              {titleShape}
              {string.Join('\n', bodyShapes ?? [])}
            </p:spTree>
          </p:cSld>
        </p:sld>
        """;

    private static string TitleShape(string text) => $$"""
        <p:sp>
          <p:nvSpPr><p:cNvPr id="2" name="Title"/><p:cNvSpPr/><p:nvPr><p:ph type="title"/></p:nvPr></p:nvSpPr>
          <p:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="100" cy="100"/></a:xfrm></p:spPr>
          <p:txBody><a:p><a:r><a:t>{{text}}</a:t></a:r></a:p></p:txBody>
        </p:sp>
        """;

    private static string BulletsShape(IReadOnlyList<string> bullets) => $"""
        <p:sp>
          <p:nvSpPr><p:cNvPr id="3" name="Content"/><p:cNvSpPr/><p:nvPr><p:ph type="body"/></p:nvPr></p:nvSpPr>
          <p:spPr><a:xfrm><a:off x="0" y="200"/><a:ext cx="100" cy="100"/></a:xfrm></p:spPr>
          <p:txBody>
            {string.Join('\n', bullets.Select(bullet => $"""<a:p><a:pPr><a:buChar char="•"/></a:pPr><a:r><a:t>{bullet}</a:t></a:r></a:p>"""))}
          </p:txBody>
        </p:sp>
        """;

    private static string ParagraphShapeAt(string text, int x, int y) => $$"""
        <p:sp>
          <p:nvSpPr><p:cNvPr id="4" name="Text"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr>
          <p:spPr><a:xfrm><a:off x="{{x}}" y="{{y}}"/><a:ext cx="100" cy="100"/></a:xfrm></p:spPr>
          <p:txBody><a:p><a:r><a:t>{{text}}</a:t></a:r></a:p></p:txBody>
        </p:sp>
        """;

    private static string TableShape(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var headerRow = "<a:tr>" + string.Join(
            string.Empty,
            headers.Select(header => $"<a:tc><a:txBody><a:p><a:r><a:t>{header}</a:t></a:r></a:p></a:txBody></a:tc>")) + "</a:tr>";
        var dataRows = string.Join(
            string.Empty,
            rows.Select(row => "<a:tr>" + string.Join(
                string.Empty,
                row.Select(value => $"<a:tc><a:txBody><a:p><a:r><a:t>{value}</a:t></a:r></a:p></a:txBody></a:tc>")) + "</a:tr>"));

        return $"""
            <p:graphicFrame>
              <p:xfrm><a:off x="0" y="0"/><a:ext cx="100" cy="100"/></p:xfrm>
              <a:graphic>
                <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/table">
                  <a:tbl>{headerRow}{dataRows}</a:tbl>
                </a:graphicData>
              </a:graphic>
            </p:graphicFrame>
            """;
    }

    private static string PictureShapeWithAltText(string? altText)
    {
        var descrAttribute = altText is null ? string.Empty : $"""descr="{altText}" """;
        return $"""
            <p:pic>
              <p:nvPicPr><p:cNvPr id="5" name="Picture" {descrAttribute}/><p:cNvPicPr/><p:nvPr/></p:nvPicPr>
              <p:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="100" cy="100"/></a:xfrm></p:spPr>
            </p:pic>
            """;
    }
}
