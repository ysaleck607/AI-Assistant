using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using AssistantCore.ExternalServices.Entities.Microsoft;

namespace AssistantCore.ExternalServices.Services.Microsoft;

public sealed class MicrosoftPowerPointContentExtractorClient
{
    private const string PresentationNamespace = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private const string DrawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string OfficeRelationshipsNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string NotesSlideRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/notesSlide";
    private static readonly XNamespace P = PresentationNamespace;
    private static readonly XNamespace A = DrawingNamespace;
    private static readonly XNamespace R = OfficeRelationshipsNamespace;
    private static readonly byte[] CompoundDocumentSignature =
        [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly HashSet<string> SupportedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/octet-stream",
        "application/zip",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/vnd.ms-powerpoint.presentation.macroenabled.12"
    };

    public async Task<MicrosoftPowerPointExtractionResult> ExtractAsync(
        string fileName,
        string? mimeType,
        Stream content,
        long? contentLength,
        long maximumFileSizeBytes,
        long maximumExpandedSizeBytes,
        int maximumExtractedCharacters,
        int maximumSlides,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);

        if (maximumFileSizeBytes <= 0
            || maximumExpandedSizeBytes <= 0
            || maximumExtractedCharacters <= 0
            || maximumSlides <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumFileSizeBytes),
                "Extraction limits must be greater than zero.");
        }

        var extension = Path.GetExtension(fileName);
        if (!extension.Equals(".pptx", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".pptm", StringComparison.OrdinalIgnoreCase))
        {
            return Result(MicrosoftPowerPointExtractionStatus.UnsupportedFormat);
        }

        if (!string.IsNullOrWhiteSpace(mimeType)
            && !SupportedMimeTypes.Contains(mimeType.Split(';', 2)[0].Trim()))
        {
            return Result(MicrosoftPowerPointExtractionStatus.UnsupportedFormat);
        }

        if (contentLength is < 0 || contentLength > maximumFileSizeBytes)
        {
            return Result(MicrosoftPowerPointExtractionStatus.TooLarge);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await using var packageStream = await CopyWithinLimitAsync(
            content,
            maximumFileSizeBytes,
            cancellationToken);
        if (packageStream is null)
        {
            return Result(MicrosoftPowerPointExtractionStatus.TooLarge);
        }

        if (HasSignature(packageStream, CompoundDocumentSignature))
        {
            return Result(MicrosoftPowerPointExtractionStatus.EncryptedDocument);
        }

        if (!HasZipSignature(packageStream))
        {
            return Result(MicrosoftPowerPointExtractionStatus.CorruptedDocument);
        }

        try
        {
            using var package = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
            if (ExceedsExpandedSizeLimit(package.Entries, maximumExpandedSizeBytes))
            {
                return Result(MicrosoftPowerPointExtractionStatus.TooLarge);
            }

            var presentationEntry = FindEntry(package, "ppt/presentation.xml");
            if (presentationEntry is null)
            {
                return Result(MicrosoftPowerPointExtractionStatus.CorruptedDocument);
            }

            var slidePaths = await ResolveSlidePathsInOrderAsync(package, cancellationToken);
            if (slidePaths.Count == 0)
            {
                return Result(MicrosoftPowerPointExtractionStatus.CorruptedDocument);
            }

            if (slidePaths.Count > maximumSlides)
            {
                return Result(MicrosoftPowerPointExtractionStatus.TooLarge);
            }

            var warnings = DetectWarnings(package);
            var units = new List<MicrosoftPowerPointExtractedUnit>();
            var characterCount = 0;
            var slideNumber = 0;

            foreach (var slidePath in slidePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                slideNumber++;
                var slideEntry = FindEntry(package, slidePath);
                if (slideEntry is null)
                {
                    continue;
                }

                await ExtractSlideAsync(
                    package,
                    slideEntry,
                    slideNumber,
                    units,
                    maximumExtractedCharacters,
                    () => characterCount,
                    value => characterCount = value,
                    cancellationToken);
            }

            return units.Count == 0
                ? new MicrosoftPowerPointExtractionResult(
                    MicrosoftPowerPointExtractionStatus.EmptyDocument,
                    [],
                    warnings)
                : new MicrosoftPowerPointExtractionResult(
                    MicrosoftPowerPointExtractionStatus.Success,
                    units,
                    warnings);
        }
        catch (ExtractionLimitExceededException)
        {
            return Result(MicrosoftPowerPointExtractionStatus.TooLarge);
        }
        catch (InvalidDataException)
        {
            return Result(MicrosoftPowerPointExtractionStatus.CorruptedDocument);
        }
        catch (XmlException)
        {
            return Result(MicrosoftPowerPointExtractionStatus.CorruptedDocument);
        }
    }

    private static async Task<IReadOnlyList<string>> ResolveSlidePathsInOrderAsync(
        ZipArchive package,
        CancellationToken cancellationToken)
    {
        var presentationEntry = FindEntry(package, "ppt/presentation.xml")!;
        var presentationRoot = await LoadXmlAsync(presentationEntry, cancellationToken);
        var relationshipIds = presentationRoot
            .Element(P + "sldIdLst")?
            .Elements(P + "sldId")
            .Select(element => (string?)element.Attribute(R + "id"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToArray() ?? [];

        if (relationshipIds.Length == 0)
        {
            return [];
        }

        var relationshipsEntry = FindEntry(package, "ppt/_rels/presentation.xml.rels");
        if (relationshipsEntry is null)
        {
            return [];
        }

        var relationshipTargets = await LoadRelationshipTargetsAsync(relationshipsEntry, cancellationToken);

        var slidePaths = new List<string>();
        foreach (var relationshipId in relationshipIds)
        {
            if (relationshipTargets.TryGetValue(relationshipId, out var target))
            {
                slidePaths.Add(ResolveRelativePath("ppt/presentation.xml", target));
            }
        }

        return slidePaths;
    }

    private static async Task<Dictionary<string, string>> LoadRelationshipTargetsAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        var root = await LoadXmlAsync(entry, cancellationToken);
        var targets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relationship in root.Elements().Where(element => element.Name.LocalName == "Relationship"))
        {
            var id = (string?)relationship.Attribute("Id");
            var target = (string?)relationship.Attribute("Target");
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(target))
            {
                targets[id] = target;
            }
        }

        return targets;
    }

    private static async Task<string?> ResolveNotesSlidePathAsync(
        ZipArchive package,
        string slidePath,
        CancellationToken cancellationToken)
    {
        var directory = GetDirectory(slidePath);
        var fileName = slidePath[(directory.Length == 0 ? 0 : directory.Length + 1)..];
        var relationshipsPath = directory.Length == 0
            ? $"_rels/{fileName}.rels"
            : $"{directory}/_rels/{fileName}.rels";

        var relationshipsEntry = FindEntry(package, relationshipsPath);
        if (relationshipsEntry is null)
        {
            return null;
        }

        var root = await LoadXmlAsync(relationshipsEntry, cancellationToken);
        var notesRelationship = root.Elements()
            .Where(element => element.Name.LocalName == "Relationship")
            .FirstOrDefault(element =>
                string.Equals((string?)element.Attribute("Type"), NotesSlideRelationshipType, StringComparison.Ordinal));

        var target = (string?)notesRelationship?.Attribute("Target");
        return string.IsNullOrWhiteSpace(target) ? null : ResolveRelativePath(slidePath, target);
    }

    private static async Task ExtractSlideAsync(
        ZipArchive package,
        ZipArchiveEntry slideEntry,
        int slideNumber,
        List<MicrosoftPowerPointExtractedUnit> units,
        int maximumCharacters,
        Func<int> getCharacterCount,
        Action<int> setCharacterCount,
        CancellationToken cancellationToken)
    {
        var root = await LoadXmlAsync(slideEntry, cancellationToken);
        var shapeTree = root.Element(P + "cSld")?.Element(P + "spTree");
        if (shapeTree is null)
        {
            return;
        }

        var shapes = new List<PowerPointShape>();
        CollectShapes(shapeTree, shapes);

        var titleShape = shapes.FirstOrDefault(IsTitlePlaceholder);
        if (titleShape is not null)
        {
            var titleText = GetShapeText(titleShape.Element);
            if (titleText.Length > 0)
            {
                AddUnit(
                    units,
                    MicrosoftPowerPointExtractedUnitKind.Title,
                    slideNumber,
                    $"Diapositive {slideNumber} — {titleText}",
                    maximumCharacters,
                    getCharacterCount,
                    setCharacterCount);
            }
        }

        var orderedShapes = shapes
            .Where(shape => shape != titleShape)
            .OrderBy(shape => shape.HasPosition ? 0 : 1)
            .ThenBy(shape => shape.OffsetY)
            .ThenBy(shape => shape.OffsetX)
            .ThenBy(shape => shape.DocumentOrder);

        foreach (var shape in orderedShapes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (shape.IsTable)
            {
                AddTable(shape.Element, slideNumber, units, maximumCharacters, getCharacterCount, setCharacterCount);
                continue;
            }

            var paragraphs = shape.Element.Element(P + "txBody")?.Elements(A + "p") ?? [];
            var hasOwnText = false;
            foreach (var paragraph in paragraphs)
            {
                var text = GetParagraphText(paragraph);
                if (text.Length == 0)
                {
                    continue;
                }

                hasOwnText = true;
                var kind = IsBulletedParagraph(paragraph)
                    ? MicrosoftPowerPointExtractedUnitKind.ListItem
                    : MicrosoftPowerPointExtractedUnitKind.Paragraph;
                var value = kind == MicrosoftPowerPointExtractedUnitKind.ListItem ? $"- {text}" : text;
                AddUnit(units, kind, slideNumber, value, maximumCharacters, getCharacterCount, setCharacterCount);
            }

            if (!hasOwnText)
            {
                var altText = GetAlternativeText(shape.Element);
                if (altText is { Length: > 0 })
                {
                    AddUnit(
                        units,
                        MicrosoftPowerPointExtractedUnitKind.Paragraph,
                        slideNumber,
                        altText,
                        maximumCharacters,
                        getCharacterCount,
                        setCharacterCount);
                }
            }
        }

        var notesPath = await ResolveNotesSlidePathAsync(package, slideEntry.FullName, cancellationToken);
        if (notesPath is not null)
        {
            var notesEntry = FindEntry(package, notesPath);
            if (notesEntry is not null)
            {
                await ExtractNotesAsync(
                    notesEntry,
                    slideNumber,
                    units,
                    maximumCharacters,
                    getCharacterCount,
                    setCharacterCount,
                    cancellationToken);
            }
        }
    }

    private static async Task ExtractNotesAsync(
        ZipArchiveEntry notesEntry,
        int slideNumber,
        List<MicrosoftPowerPointExtractedUnit> units,
        int maximumCharacters,
        Func<int> getCharacterCount,
        Action<int> setCharacterCount,
        CancellationToken cancellationToken)
    {
        var root = await LoadXmlAsync(notesEntry, cancellationToken);
        var shapeTree = root.Element(P + "cSld")?.Element(P + "spTree");
        if (shapeTree is null)
        {
            return;
        }

        foreach (var shape in shapeTree.Elements(P + "sp"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var placeholderType = (string?)shape
                .Element(P + "nvSpPr")?
                .Element(P + "nvPr")?
                .Element(P + "ph")?
                .Attribute("type");

            if (!string.Equals(placeholderType, "body", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var paragraph in shape.Element(P + "txBody")?.Elements(A + "p") ?? [])
            {
                var text = GetParagraphText(paragraph);
                if (text.Length == 0)
                {
                    continue;
                }

                AddUnit(
                    units,
                    MicrosoftPowerPointExtractedUnitKind.Note,
                    slideNumber,
                    $"Notes : {text}",
                    maximumCharacters,
                    getCharacterCount,
                    setCharacterCount);
            }
        }
    }

    private static void CollectShapes(XElement container, List<PowerPointShape> shapes)
    {
        var index = 0;
        foreach (var element in container.Elements())
        {
            if (element.Name == P + "grpSp")
            {
                CollectShapes(element, shapes);
                continue;
            }

            if (element.Name != P + "sp"
                && element.Name != P + "pic"
                && element.Name != P + "graphicFrame")
            {
                continue;
            }

            var isTable = element.Name == P + "graphicFrame"
                && element.Descendants(A + "tbl").Any();
            var transform = element.Name == P + "graphicFrame"
                ? element.Element(P + "xfrm")
                : element.Element(P + "spPr")?.Element(A + "xfrm");
            var offset = transform?.Element(A + "off");
            var offsetX = (long?)offset?.Attribute("x");
            var offsetY = (long?)offset?.Attribute("y");

            shapes.Add(new PowerPointShape(
                element,
                isTable,
                offsetX.HasValue && offsetY.HasValue,
                offsetX ?? 0,
                offsetY ?? 0,
                index++));
        }
    }

    private static bool IsTitlePlaceholder(PowerPointShape shape)
    {
        if (shape.Element.Name != P + "sp")
        {
            return false;
        }

        var placeholderType = (string?)shape.Element
            .Element(P + "nvSpPr")?
            .Element(P + "nvPr")?
            .Element(P + "ph")?
            .Attribute("type");

        return string.Equals(placeholderType, "title", StringComparison.OrdinalIgnoreCase)
            || string.Equals(placeholderType, "ctrTitle", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetAlternativeText(XElement shape)
    {
        var descr = shape.Name == P + "pic"
            ? (string?)shape.Element(P + "nvPicPr")?.Element(P + "cNvPr")?.Attribute("descr")
            : (string?)shape.Element(P + "nvSpPr")?.Element(P + "cNvPr")?.Attribute("descr")
                ?? (string?)shape.Element(P + "nvGraphicFramePr")?.Element(P + "cNvPr")?.Attribute("descr");

        return string.IsNullOrWhiteSpace(descr) ? null : Normalize(descr);
    }

    private static string GetShapeText(XElement shape) =>
        Normalize(string.Join(
            " ",
            (shape.Element(P + "txBody")?.Elements(A + "p") ?? []).Select(GetParagraphText)));

    private static bool IsBulletedParagraph(XElement paragraph)
    {
        var properties = paragraph.Element(A + "pPr");
        return properties?.Element(A + "buChar") is not null
            || properties?.Element(A + "buAutoNum") is not null;
    }

    private static string GetParagraphText(XElement paragraph)
    {
        var builder = new StringBuilder();
        foreach (var element in paragraph.Descendants())
        {
            if (element.Name == A + "t")
            {
                builder.Append(element.Value);
            }
            else if (element.Name == A + "br")
            {
                builder.Append(' ');
            }
        }

        return Normalize(builder.ToString());
    }

    private static void AddTable(
        XElement graphicFrame,
        int slideNumber,
        List<MicrosoftPowerPointExtractedUnit> units,
        int maximumCharacters,
        Func<int> getCharacterCount,
        Action<int> setCharacterCount)
    {
        var table = graphicFrame.Descendants(A + "tbl").First();
        var rows = table.Elements(A + "tr")
            .Select(row => row.Elements(A + "tc")
                .Select(cell => Normalize(string.Join(
                    " ",
                    cell.Descendants(A + "p").Select(GetParagraphText))))
                .ToArray())
            .Where(row => row.Any(value => value.Length > 0))
            .ToArray();

        if (rows.Length == 0)
        {
            return;
        }

        AddUnit(
            units,
            MicrosoftPowerPointExtractedUnitKind.Table,
            slideNumber,
            "Tableau",
            maximumCharacters,
            getCharacterCount,
            setCharacterCount);

        if (rows.Length == 1)
        {
            AddUnit(
                units,
                MicrosoftPowerPointExtractedUnitKind.Table,
                slideNumber,
                string.Join(" | ", rows[0]),
                maximumCharacters,
                getCharacterCount,
                setCharacterCount);
            return;
        }

        var headers = rows[0];
        foreach (var row in rows.Skip(1))
        {
            var values = row.Select((value, index) =>
                index < headers.Length && headers[index].Length > 0
                    ? $"{headers[index]} : {value}"
                    : value);
            AddUnit(
                units,
                MicrosoftPowerPointExtractedUnitKind.Table,
                slideNumber,
                string.Join(" | ", values),
                maximumCharacters,
                getCharacterCount,
                setCharacterCount);
        }
    }

    private static void AddUnit(
        List<MicrosoftPowerPointExtractedUnit> units,
        MicrosoftPowerPointExtractedUnitKind kind,
        int slideNumber,
        string text,
        int maximumCharacters,
        Func<int> getCharacterCount,
        Action<int> setCharacterCount)
    {
        if (text.Length == 0)
        {
            return;
        }

        var newCharacterCount = checked(getCharacterCount() + text.Length);
        if (newCharacterCount > maximumCharacters)
        {
            throw new ExtractionLimitExceededException();
        }

        setCharacterCount(newCharacterCount);
        units.Add(new MicrosoftPowerPointExtractedUnit(kind, slideNumber, units.Count, text));
    }

    private static string GetDirectory(string path)
    {
        var lastSeparator = path.LastIndexOf('/');
        return lastSeparator < 0 ? string.Empty : path[..lastSeparator];
    }

    private static string ResolveRelativePath(string basePath, string relativeTarget)
    {
        if (relativeTarget.StartsWith('/'))
        {
            return relativeTarget.TrimStart('/');
        }

        var baseDirectory = GetDirectory(basePath);
        var combined = baseDirectory.Length == 0
            ? []
            : baseDirectory.Split('/').ToList();

        foreach (var segment in relativeTarget.Split('/'))
        {
            if (segment == "..")
            {
                if (combined.Count > 0)
                {
                    combined.RemoveAt(combined.Count - 1);
                }
            }
            else if (segment != "." && segment.Length > 0)
            {
                combined.Add(segment);
            }
        }

        return string.Join('/', combined);
    }

    private static async Task<MemoryStream?> CopyWithinLimitAsync(
        Stream source,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var destination = new MemoryStream();
        var buffer = new byte[81920];
        long totalBytes = 0;

        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                destination.Position = 0;
                return destination;
            }

            totalBytes += bytesRead;
            if (totalBytes > maximumBytes)
            {
                await destination.DisposeAsync();
                return null;
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }
    }

    private static bool HasSignature(Stream content, byte[] signature)
    {
        content.Position = 0;
        Span<byte> actual = stackalloc byte[signature.Length];
        var bytesRead = content.Read(actual);
        content.Position = 0;
        return bytesRead == signature.Length
            && actual.SequenceEqual(signature);
    }

    private static bool HasZipSignature(Stream content)
    {
        content.Position = 0;
        Span<byte> signature = stackalloc byte[4];
        var bytesRead = content.Read(signature);
        content.Position = 0;
        return bytesRead == signature.Length
            && signature[0] == 0x50
            && signature[1] == 0x4B
            && ((signature[2] == 0x03 && signature[3] == 0x04)
                || (signature[2] == 0x05 && signature[3] == 0x06)
                || (signature[2] == 0x07 && signature[3] == 0x08));
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive package, string path) =>
        package.Entries.FirstOrDefault(entry =>
            entry.FullName.Equals(path, StringComparison.OrdinalIgnoreCase));

    private static bool ExceedsExpandedSizeLimit(
        IReadOnlyCollection<ZipArchiveEntry> entries,
        long maximumExpandedSizeBytes)
    {
        long totalSize = 0;
        foreach (var entry in entries)
        {
            if (entry.Length > maximumExpandedSizeBytes - totalSize)
            {
                return true;
            }

            totalSize += entry.Length;
        }

        return false;
    }

    private static IReadOnlyList<MicrosoftPowerPointExtractionWarning> DetectWarnings(ZipArchive package)
    {
        var warnings = new HashSet<MicrosoftPowerPointExtractionWarning>();
        if (package.Entries.Any(entry =>
                entry.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add(MicrosoftPowerPointExtractionWarning.MacroIgnored);
        }

        if (package.Entries.Any(entry =>
                entry.FullName.StartsWith("ppt/embeddings/", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add(MicrosoftPowerPointExtractionWarning.EmbeddedObjectIgnored);
        }

        if (package.Entries
            .Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            .Any(ContainsExternalRelationship))
        {
            warnings.Add(MicrosoftPowerPointExtractionWarning.ExternalLinkIgnored);
        }

        return warnings.OrderBy(warning => warning).ToArray();
    }

    private static bool ContainsExternalRelationship(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = CreateSecureXmlReader(stream);
        var document = XDocument.Load(reader, LoadOptions.None);
        return document.Descendants().Any(element =>
            string.Equals(
                element.Attribute("TargetMode")?.Value,
                "External",
                StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<XElement> LoadXmlAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = entry.Open();
        using var reader = CreateSecureXmlReader(stream);
        var document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
        return document.Root ?? throw new XmlException("The PowerPoint XML part has no root element.");
    }

    private static XmlReader CreateSecureXmlReader(Stream stream) =>
        XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 100_000_000
        });

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static MicrosoftPowerPointExtractionResult Result(MicrosoftPowerPointExtractionStatus status) =>
        new(status, [], []);

    private sealed record PowerPointShape(
        XElement Element,
        bool IsTable,
        bool HasPosition,
        long OffsetX,
        long OffsetY,
        int DocumentOrder);

    private sealed class ExtractionLimitExceededException : Exception;
}
