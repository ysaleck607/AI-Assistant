using AssistantCore.ExternalServices.Entities.Microsoft;
using AssistantCore.ExternalServices.Services.Microsoft;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Models.Microsoft365.ContentExtraction;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Infrastructure.Microsoft365;

public sealed class Microsoft365ArchiveContentExtractorAdapter(
    MicrosoftArchiveContentExtractorClient archiveClient,
    IEnumerable<IMicrosoft365ContentExtractor> extractors,
    IOptions<Microsoft365Options> options) : IMicrosoft365ArchiveContentExtractor
{
    private static readonly HashSet<string> ArchiveExtensions = [".zip", ".gz"];
    private static readonly HashSet<string> SupportedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/zip", "application/gzip", "application/x-gzip", "application/x-zip-compressed", "application/octet-stream"
    };
    private static readonly HashSet<string> AudioVideoExtensions = [".avi", ".mov", ".mp3", ".mp4", ".mpeg", ".wav", ".webm", ".wmv"];
    private readonly IReadOnlyList<IMicrosoft365ContentExtractor> _extractors = extractors.ToArray();

    public bool CanExtract(string fileName, string? mimeType) =>
        ArchiveExtensions.Contains(Path.GetExtension(fileName))
        && (string.IsNullOrWhiteSpace(mimeType)
            || SupportedMimeTypes.Contains(mimeType.Split(';', 2)[0].Trim()));

    public async Task<Microsoft365ContentExtractionResult> ExtractAsync(
        Microsoft365ContentExtractionRequest request,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.ArchiveExtractionTimeoutSeconds));
        var warnings = new List<Microsoft365ContentExtractionWarning>();
        try
        {
            var units = await ExtractLevelAsync(request, string.Empty, 0, warnings, timeout.Token);
            return new(units.Count == 0 ? MapEmptyStatus(warnings) : Microsoft365ContentExtractionStatus.Success,
                units.Select((unit, index) => unit with { Order = index }).ToArray(), warnings.Distinct().ToArray());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            warnings.Add(Microsoft365ContentExtractionWarning.PartialArchive);
            return new(Microsoft365ContentExtractionStatus.ArchiveTimeout, [], warnings.Distinct().ToArray());
        }
    }

    private async Task<List<Microsoft365ExtractedContentUnit>> ExtractLevelAsync(
        Microsoft365ContentExtractionRequest request,
        string archivePrefix,
        int depth,
        List<Microsoft365ContentExtractionWarning> warnings,
        CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        var extraction = await archiveClient.ExtractAsync(
            request.FileName,
            request.Content,
            request.ContentLength,
            configuration.MaximumArchiveCompressedSizeBytes,
            configuration.MaximumArchiveExpandedSizeBytes,
            configuration.MaximumArchiveEntries,
            configuration.MaximumArchiveCompressionRatio,
            cancellationToken);
        AddWarnings(extraction.Warnings, warnings);
        if (extraction.Status != MicrosoftArchiveExtractionStatus.Success)
        {
            AddStatusWarning(extraction.Status, warnings);
            return [];
        }

        var units = new List<Microsoft365ExtractedContentUnit>();
        foreach (var entry in extraction.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var archivePath = CombinePath(archivePrefix, entry.ArchivePath);
            if (AudioVideoExtensions.Contains(Path.GetExtension(entry.FileName)))
            {
                warnings.Add(Microsoft365ContentExtractionWarning.UnsupportedArchiveEntry);
                warnings.Add(Microsoft365ContentExtractionWarning.PartialArchive);
                continue;
            }

            if (CanExtractArchive(entry.FileName, entry.MimeType))
            {
                if (depth >= configuration.MaximumArchiveDepth)
                {
                    warnings.Add(Microsoft365ContentExtractionWarning.ArchiveDepthExceeded);
                    warnings.Add(Microsoft365ContentExtractionWarning.PartialArchive);
                    continue;
                }
                await using var nestedContent = new MemoryStream(entry.Content, writable: false);
                units.AddRange(await ExtractLevelAsync(
                    new Microsoft365ContentExtractionRequest(entry.FileName, entry.MimeType, nestedContent, entry.Content.Length),
                    archivePath,
                    depth + 1,
                    warnings,
                    cancellationToken));
                continue;
            }

            var matching = _extractors.Where(extractor => extractor.CanExtract(entry.FileName, entry.MimeType)).ToArray();
            if (matching.Length != 1)
            {
                warnings.Add(Microsoft365ContentExtractionWarning.UnsupportedArchiveEntry);
                warnings.Add(Microsoft365ContentExtractionWarning.PartialArchive);
                continue;
            }

            await using var entryContent = new MemoryStream(entry.Content, writable: false);
            var result = await matching[0].ExtractAsync(
                new Microsoft365ContentExtractionRequest(entry.FileName, entry.MimeType, entryContent, entry.Content.Length),
                cancellationToken);
            if (result.Status != Microsoft365ContentExtractionStatus.Success)
            {
                warnings.Add(Microsoft365ContentExtractionWarning.CorruptedArchiveEntry);
                warnings.Add(Microsoft365ContentExtractionWarning.PartialArchive);
                continue;
            }
            units.AddRange(result.Units.Select(unit => unit with
            {
                ArchivePath = archivePath,
                SourcePart = $"{archivePath}/{unit.SourcePart}"
            }));
        }
        return units;
    }

    private bool CanExtractArchive(string fileName, string? mimeType) => CanExtract(fileName, mimeType);

    private static Microsoft365ContentExtractionStatus MapEmptyStatus(IReadOnlyCollection<Microsoft365ContentExtractionWarning> warnings) =>
        warnings.Contains(Microsoft365ContentExtractionWarning.UnsafeArchivePath) ? Microsoft365ContentExtractionStatus.UnsafeArchivePath
        : warnings.Contains(Microsoft365ContentExtractionWarning.ArchiveTooLarge) ? Microsoft365ContentExtractionStatus.ArchiveTooLarge
        : warnings.Contains(Microsoft365ContentExtractionWarning.EncryptedArchive) ? Microsoft365ContentExtractionStatus.EncryptedArchive
        : warnings.Contains(Microsoft365ContentExtractionWarning.CorruptedArchive) ? Microsoft365ContentExtractionStatus.CorruptedArchive
        : warnings.Contains(Microsoft365ContentExtractionWarning.ArchiveDepthExceeded) ? Microsoft365ContentExtractionStatus.ArchiveDepthExceeded
        : Microsoft365ContentExtractionStatus.NoIndexableContent;

    private static void AddWarnings(IEnumerable<MicrosoftArchiveExtractionWarning> source, ICollection<Microsoft365ContentExtractionWarning> target)
    {
        foreach (var warning in source)
        {
            target.Add(warning switch
            {
                MicrosoftArchiveExtractionWarning.UnsafeArchivePath => Microsoft365ContentExtractionWarning.UnsafeArchivePath,
                MicrosoftArchiveExtractionWarning.DuplicateArchiveEntry => Microsoft365ContentExtractionWarning.DuplicateArchiveEntry,
                MicrosoftArchiveExtractionWarning.CorruptedArchiveEntry => Microsoft365ContentExtractionWarning.CorruptedArchiveEntry,
                _ => Microsoft365ContentExtractionWarning.PartialArchive
            });
        }
    }

    private static void AddStatusWarning(MicrosoftArchiveExtractionStatus status, ICollection<Microsoft365ContentExtractionWarning> warnings)
    {
        var warning = status switch
        {
            MicrosoftArchiveExtractionStatus.TooLarge => Microsoft365ContentExtractionWarning.ArchiveTooLarge,
            MicrosoftArchiveExtractionStatus.EncryptedArchive => Microsoft365ContentExtractionWarning.EncryptedArchive,
            MicrosoftArchiveExtractionStatus.CorruptedArchive => Microsoft365ContentExtractionWarning.CorruptedArchive,
            MicrosoftArchiveExtractionStatus.UnsafeArchivePath => Microsoft365ContentExtractionWarning.UnsafeArchivePath,
            MicrosoftArchiveExtractionStatus.ArchiveDepthExceeded => Microsoft365ContentExtractionWarning.ArchiveDepthExceeded,
            _ => (Microsoft365ContentExtractionWarning?)null
        };
        if (warning.HasValue) warnings.Add(warning.Value);
    }

    private static string CombinePath(string prefix, string path) => string.IsNullOrEmpty(prefix) ? path : $"{prefix}/{path}";
}
