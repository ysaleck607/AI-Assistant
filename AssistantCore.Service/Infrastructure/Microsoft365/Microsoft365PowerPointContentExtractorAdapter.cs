using AssistantCore.ExternalServices.Entities.Microsoft;
using AssistantCore.ExternalServices.Services.Microsoft;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Models.Microsoft365.ContentExtraction;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Infrastructure.Microsoft365;

public sealed class Microsoft365PowerPointContentExtractorAdapter(
    MicrosoftPowerPointContentExtractorClient client,
    IOptions<Microsoft365Options> options) : IMicrosoft365ContentExtractor
{
    private static readonly HashSet<string> PowerPointExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ppt", ".pptx", ".pptm"
    };

    public bool CanExtract(string fileName, string? mimeType) =>
        !string.IsNullOrWhiteSpace(fileName)
        && PowerPointExtensions.Contains(Path.GetExtension(fileName));

    public async Task<Microsoft365ContentExtractionResult> ExtractAsync(
        Microsoft365ContentExtractionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await client.ExtractAsync(
            request.FileName,
            request.MimeType,
            request.Content,
            request.ContentLength,
            options.Value.MaximumExtractionFileSizeBytes,
            options.Value.MaximumExtractionExpandedSizeBytes,
            options.Value.MaximumExtractedCharacters,
            options.Value.MaximumPowerPointSlides,
            cancellationToken);

        return new Microsoft365ContentExtractionResult(
            MapStatus(result.Status),
            result.Units.Select(MapUnit).ToArray(),
            result.Warnings.Select(MapWarning).ToArray());
    }

    private static Microsoft365ContentExtractionStatus MapStatus(
        MicrosoftPowerPointExtractionStatus status) => status switch
    {
        MicrosoftPowerPointExtractionStatus.Success => Microsoft365ContentExtractionStatus.Success,
        MicrosoftPowerPointExtractionStatus.EmptyDocument => Microsoft365ContentExtractionStatus.EmptyDocument,
        MicrosoftPowerPointExtractionStatus.EncryptedDocument => Microsoft365ContentExtractionStatus.EncryptedDocument,
        MicrosoftPowerPointExtractionStatus.CorruptedDocument => Microsoft365ContentExtractionStatus.CorruptedDocument,
        MicrosoftPowerPointExtractionStatus.UnsupportedFormat => Microsoft365ContentExtractionStatus.UnsupportedFormat,
        MicrosoftPowerPointExtractionStatus.TooLarge => Microsoft365ContentExtractionStatus.TooLarge,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static Microsoft365ExtractedContentUnit MapUnit(MicrosoftPowerPointExtractedUnit unit) =>
        new(
            unit.Kind switch
            {
                MicrosoftPowerPointExtractedUnitKind.Title => Microsoft365ExtractedContentUnitKind.Title,
                MicrosoftPowerPointExtractedUnitKind.Paragraph => Microsoft365ExtractedContentUnitKind.Paragraph,
                MicrosoftPowerPointExtractedUnitKind.ListItem => Microsoft365ExtractedContentUnitKind.ListItem,
                MicrosoftPowerPointExtractedUnitKind.Table => Microsoft365ExtractedContentUnitKind.Table,
                MicrosoftPowerPointExtractedUnitKind.Note => Microsoft365ExtractedContentUnitKind.Note,
                _ => throw new ArgumentOutOfRangeException(nameof(unit), unit.Kind, null)
            },
            unit.Order,
            unit.Text,
            $"slide:{unit.SlideNumber}");

    private static Microsoft365ContentExtractionWarning MapWarning(
        MicrosoftPowerPointExtractionWarning warning) => warning switch
    {
        MicrosoftPowerPointExtractionWarning.MacroIgnored => Microsoft365ContentExtractionWarning.MacroIgnored,
        MicrosoftPowerPointExtractionWarning.ExternalLinkIgnored => Microsoft365ContentExtractionWarning.ExternalLinkIgnored,
        MicrosoftPowerPointExtractionWarning.EmbeddedObjectIgnored => Microsoft365ContentExtractionWarning.EmbeddedObjectIgnored,
        _ => throw new ArgumentOutOfRangeException(nameof(warning), warning, null)
    };
}
