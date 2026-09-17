using System.Security.Cryptography;
using System.Text;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Models.Microsoft365.ContentExtraction;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public sealed class Microsoft365DocumentChunkingService(IOptions<Microsoft365Options> options)
    : IMicrosoft365DocumentChunkingService
{
    public IReadOnlyList<Microsoft365SearchPassage> CreateChunks(
        Guid organizationId,
        Guid sourceId,
        string siteId,
        string driveId,
        string driveItemId,
        string documentVersion,
        string title,
        string? url,
        DateTimeOffset? modifiedAt,
        IReadOnlyCollection<Microsoft365ExtractedContentUnit> units)
    {
        var chunks = new List<Microsoft365SearchPassage>();
        foreach (var group in units.GroupBy(unit => unit.ArchivePath ?? string.Empty))
        {
            chunks.AddRange(CreateChunksForUnits(
                organizationId, sourceId, siteId, driveId, driveItemId, documentVersion,
                title, url, modifiedAt, group.OrderBy(unit => unit.Order).ToArray(), group.Key, chunks.Count));
        }
        return chunks;
    }

    private IReadOnlyList<Microsoft365SearchPassage> CreateChunksForUnits(
        Guid organizationId, Guid sourceId, string siteId, string driveId, string driveItemId,
        string documentVersion, string title, string? url, DateTimeOffset? modifiedAt,
        IReadOnlyCollection<Microsoft365ExtractedContentUnit> orderedUnits, string archivePath, int chunkOffset)
    {
        var maximumCharacters = checked(options.Value.ChunkMaximumTokens * 4);
        var overlapCharacters = checked(options.Value.ChunkOverlapTokens * 4);
        var text = string.Join(Environment.NewLine, orderedUnits.Select(unit => unit.Text));
        var sectionPositions = FindSectionPositions(orderedUnits);
        if (string.IsNullOrWhiteSpace(text)) return [];
        var chunks = new List<Microsoft365SearchPassage>();
        var position = 0;
        while (position < text.Length && chunks.Count < options.Value.MaximumChunksPerDocument)
        {
            var length = Math.Min(maximumCharacters, text.Length - position);
            if (position + length < text.Length)
            {
                var boundary = text.LastIndexOfAny(
                    ['\n', '.', '!', '?', ';'],
                    position + length - 1,
                    length);
                if (boundary > position + maximumCharacters / 2)
                {
                    length = boundary - position + 1;
                }
            }

            var content = AddSectionContext(
                text.Substring(position, length).Trim(),
                sectionPositions.LastOrDefault(section => section.Position <= position).Title,
                maximumCharacters);
            if (content.Length > 0)
            {
                var chunkNumber = chunkOffset + chunks.Count;
                chunks.Add(new Microsoft365SearchPassage(
                    CreateChunkId(organizationId, sourceId, driveItemId, documentVersion, chunkNumber, archivePath),
                    title,
                    content,
                    siteId,
                    driveId,
                    driveItemId,
                    documentVersion,
                    chunkNumber,
                    url,
                    modifiedAt,
                    ArchivePath: string.IsNullOrEmpty(archivePath) ? null : archivePath));
            }

            if (position + length >= text.Length)
            {
                break;
            }

            position += Math.Max(1, length - overlapCharacters);
        }

        return chunks;
    }

    private static IReadOnlyList<(int Position, string Title)> FindSectionPositions(
        IReadOnlyCollection<Microsoft365ExtractedContentUnit> units)
    {
        var sections = new List<(int Position, string Title)>();
        var position = 0;
        foreach (var unit in units)
        {
            if (unit.Kind is Microsoft365ExtractedContentUnitKind.Header
                or Microsoft365ExtractedContentUnitKind.Title)
            {
                sections.Add((position, unit.Text.Trim()));
            }

            position += unit.Text.Length + Environment.NewLine.Length;
        }

        return sections;
    }

    private static string AddSectionContext(
        string content,
        string? sectionTitle,
        int maximumCharacters)
    {
        if (string.IsNullOrWhiteSpace(sectionTitle)
            || content.StartsWith(sectionTitle, StringComparison.OrdinalIgnoreCase))
        {
            return content;
        }

        var prefix = $"Section: {sectionTitle}{Environment.NewLine}";
        if (prefix.Length >= maximumCharacters)
        {
            return content;
        }

        return prefix + content[..Math.Min(content.Length, maximumCharacters - prefix.Length)];
    }

    private static string CreateChunkId(
        Guid organizationId,
        Guid sourceId,
        string driveItemId,
        string version,
        int chunkNumber,
        string archivePath)
    {
        var identity = $"{organizationId:N}|{sourceId:N}|{driveItemId}|{version}|{chunkNumber}"
            + (string.IsNullOrEmpty(archivePath) ? string.Empty : $"|{archivePath}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }
}
