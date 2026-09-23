using System.IO.Compression;
using AssistantCore.ExternalServices.Entities.Microsoft;

namespace AssistantCore.ExternalServices.Services.Microsoft;

public sealed class MicrosoftArchiveContentExtractorClient
{
    private static readonly byte[] ZipSignature = [0x50, 0x4B];
    private static readonly byte[] GZipSignature = [0x1F, 0x8B];

    public async Task<MicrosoftArchiveExtractionResult> ExtractAsync(
        string fileName,
        Stream content,
        long? contentLength,
        long maximumCompressedSizeBytes,
        long maximumExpandedSizeBytes,
        int maximumEntries,
        double maximumCompressionRatio,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);
        if (maximumCompressedSizeBytes <= 0 || maximumExpandedSizeBytes <= 0
            || maximumEntries <= 0 || !double.IsFinite(maximumCompressionRatio)
            || maximumCompressionRatio <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCompressedSizeBytes));
        }

        if (contentLength is < 0 || contentLength > maximumCompressedSizeBytes)
        {
            return TooLarge();
        }

        await using var input = new MemoryStream();
        if (!await CopyBoundedAsync(content, input, maximumCompressedSizeBytes, cancellationToken))
        {
            return TooLarge();
        }

        var compressedSize = input.Length;
        input.Position = 0;
        var signature = new byte[2];
        if (await input.ReadAsync(signature, cancellationToken) != 2)
        {
            return Corrupted();
        }
        input.Position = 0;

        if (signature.SequenceEqual(ZipSignature))
        {
            return ExtractZip(input, compressedSize, maximumExpandedSizeBytes, maximumEntries, maximumCompressionRatio, cancellationToken);
        }

        if (signature.SequenceEqual(GZipSignature))
        {
            return await ExtractGZipAsync(fileName, input, compressedSize, maximumExpandedSizeBytes, maximumCompressionRatio, cancellationToken);
        }

        return Corrupted();
    }

    private static MicrosoftArchiveExtractionResult ExtractZip(
        Stream content,
        long compressedSize,
        long maximumExpandedSizeBytes,
        int maximumEntries,
        double maximumCompressionRatio,
        CancellationToken cancellationToken)
    {
        var entries = new List<MicrosoftArchiveEntry>();
        var warnings = new List<MicrosoftArchiveExtractionWarning>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expandedSize = 0;
        try
        {
            using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                {
                    continue;
                }
                if (entries.Count >= maximumEntries)
                {
                    warnings.Add(MicrosoftArchiveExtractionWarning.PartialArchive);
                    return new(MicrosoftArchiveExtractionStatus.TooLarge, entries, warnings);
                }

                var path = NormalizePath(entry.FullName);
                if (path is null)
                {
                    warnings.Add(MicrosoftArchiveExtractionWarning.UnsafeArchivePath);
                    continue;
                }
                if (!paths.Add(path))
                {
                    warnings.Add(MicrosoftArchiveExtractionWarning.DuplicateArchiveEntry);
                    continue;
                }

                if (entry.Length < 0 || entry.Length > maximumExpandedSizeBytes - expandedSize
                    || (entry.CompressedLength > 0 && (double)entry.Length > entry.CompressedLength * maximumCompressionRatio))
                {
                    warnings.Add(MicrosoftArchiveExtractionWarning.PartialArchive);
                    return new(MicrosoftArchiveExtractionStatus.TooLarge, entries, warnings);
                }

                try
                {
                    using var source = entry.Open();
                    using var extracted = new MemoryStream();
                    if (!CopyEntryBounded(source, extracted, maximumExpandedSizeBytes - expandedSize, cancellationToken))
                    {
                        warnings.Add(MicrosoftArchiveExtractionWarning.PartialArchive);
                        return new(MicrosoftArchiveExtractionStatus.TooLarge, entries, warnings);
                    }
                    expandedSize += extracted.Length;
                    var mimeType = GetMimeType(path);
                    entries.Add(new(
                        path,
                        Path.GetFileName(path),
                        RemoveUtf8Bom(extracted.ToArray(), mimeType),
                        mimeType));
                }
                catch (InvalidDataException)
                {
                    warnings.Add(MicrosoftArchiveExtractionWarning.CorruptedArchiveEntry);
                    warnings.Add(MicrosoftArchiveExtractionWarning.PartialArchive);
                }
            }
        }
        catch (NotSupportedException)
        {
            return new(MicrosoftArchiveExtractionStatus.EncryptedArchive, entries, warnings);
        }
        catch (InvalidDataException)
        {
            return new(MicrosoftArchiveExtractionStatus.CorruptedArchive, entries, warnings);
        }

        var status = entries.Count > 0
            ? MicrosoftArchiveExtractionStatus.Success
            : warnings.Contains(MicrosoftArchiveExtractionWarning.UnsafeArchivePath)
                ? MicrosoftArchiveExtractionStatus.UnsafeArchivePath
                : MicrosoftArchiveExtractionStatus.EmptyArchive;
        return new(status, entries, warnings);
    }

    private static async Task<MicrosoftArchiveExtractionResult> ExtractGZipAsync(
        string fileName,
        Stream content,
        long compressedSize,
        long maximumExpandedSizeBytes,
        double maximumCompressionRatio,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var gzip = new GZipStream(content, CompressionMode.Decompress, leaveOpen: true);
            await using var output = new MemoryStream();
            if (!await CopyBoundedAsync(gzip, output, maximumExpandedSizeBytes, cancellationToken)
                || (compressedSize > 0 && output.Length > compressedSize * maximumCompressionRatio))
            {
                return TooLarge();
            }

            if (output.Length == 0)
            {
                return new(MicrosoftArchiveExtractionStatus.EmptyArchive, [], []);
            }

            var name = Path.GetFileNameWithoutExtension(fileName);
            var mimeType = GetMimeType(name);
            return new(
                MicrosoftArchiveExtractionStatus.Success,
                [new MicrosoftArchiveEntry(
                    name,
                    name,
                    RemoveUtf8Bom(output.ToArray(), mimeType),
                    mimeType)],
                []);
        }
        catch (InvalidDataException)
        {
            return Corrupted();
        }
    }

    private static string? NormalizePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\0')) return null;
        var candidate = value.Replace('\\', '/');
        if (candidate.StartsWith("/", StringComparison.Ordinal) || candidate.StartsWith("//", StringComparison.Ordinal)
            || (candidate.Length >= 2 && candidate[1] == ':')) return null;
        var segments = candidate.Split('/');
        if (segments.Any(segment => segment is "" or "." or "..")) return null;
        return string.Join("/", segments);
    }

    private static async Task<bool> CopyBoundedAsync(Stream source, Stream destination, long maximum, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maximum) return false;
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return true;
    }

    private static bool CopyEntryBounded(Stream source, Stream destination, long maximum, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total += read;
            if (total > maximum) return false;
            destination.Write(buffer, 0, read);
        }
        return true;
    }

    private static string? GetMimeType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".txt" => "text/plain",
        ".csv" => "text/csv",
        _ => null
    };

    private static byte[] RemoveUtf8Bom(byte[] content, string? mimeType)
    {
        if (mimeType is not ("text/plain" or "text/csv")
            || content.Length < 3
            || content[0] != 0xEF
            || content[1] != 0xBB
            || content[2] != 0xBF)
        {
            return content;
        }

        return content[3..];
    }

    private static MicrosoftArchiveExtractionResult TooLarge() => new(MicrosoftArchiveExtractionStatus.TooLarge, [], []);
    private static MicrosoftArchiveExtractionResult Corrupted() => new(MicrosoftArchiveExtractionStatus.CorruptedArchive, [], []);
}
