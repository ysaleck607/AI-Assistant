using AssistantCore.Service.Application.Models.Microsoft365.ContentExtraction;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public sealed class Microsoft365ContentExtractionService(
    IEnumerable<IMicrosoft365ContentExtractor> extractors,
    IEnumerable<IMicrosoft365ArchiveContentExtractor>? archiveExtractors = null)
    : IMicrosoft365ContentExtractionService
{
    private readonly IReadOnlyList<IMicrosoft365ContentExtractor> _extractors =
        extractors?.ToArray() ?? throw new ArgumentNullException(nameof(extractors));

    private readonly IReadOnlyList<IMicrosoft365ArchiveContentExtractor> _archiveExtractors =
        archiveExtractors?.ToArray() ?? [];

    public Task<Microsoft365ContentExtractionResult> ExtractAsync(
        Microsoft365ContentExtractionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        ArgumentNullException.ThrowIfNull(request.Content);

        var matchingArchiveExtractors = _archiveExtractors
            .Where(extractor => extractor.CanExtract(request.FileName, request.MimeType))
            .ToArray();
        if (matchingArchiveExtractors.Length > 1)
        {
            throw new InvalidOperationException(
                $"More than one archive extractor supports '{Path.GetExtension(request.FileName)}'.");
        }

        if (matchingArchiveExtractors.Length == 1)
        {
            return matchingArchiveExtractors[0].ExtractAsync(request, cancellationToken);
        }

        var matchingExtractors = _extractors
            .Where(extractor => extractor.CanExtract(request.FileName, request.MimeType))
            .ToArray();

        return matchingExtractors.Length switch
        {
            0 => Task.FromResult(Microsoft365ContentExtractionResult.Unsupported()),
            1 => matchingExtractors[0].ExtractAsync(request, cancellationToken),
            _ => throw new InvalidOperationException(
                $"More than one content extractor supports '{Path.GetExtension(request.FileName)}'.")
        };
    }
}
