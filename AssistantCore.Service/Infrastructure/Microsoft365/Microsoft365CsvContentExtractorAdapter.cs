using AssistantCore.ExternalServices.Entities.Microsoft;
using AssistantCore.ExternalServices.Services.Microsoft;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Models.Microsoft365.ContentExtraction;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Infrastructure.Microsoft365;

public sealed class Microsoft365CsvContentExtractorAdapter(
    MicrosoftCsvContentExtractorClient client,
    IOptions<Microsoft365Options> options) : IMicrosoft365ContentExtractor
{
    private static readonly HashSet<string> CsvExtensions = [".csv"];

    public bool CanExtract(string fileName, string? mimeType) =>
        !string.IsNullOrWhiteSpace(fileName)
        && CsvExtensions.Contains(Path.GetExtension(fileName));

    public async Task<Microsoft365ContentExtractionResult> ExtractAsync(
        Microsoft365ContentExtractionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await client.ExtractAsync(
            request.FileName,
            request.Content,
            request.ContentLength,
            options.Value.MaximumExtractionFileSizeBytes,
            options.Value.MaximumExtractedCharacters,
            cancellationToken);

        return new(
            result.Status switch
            {
                MicrosoftCsvExtractionStatus.Success => Microsoft365ContentExtractionStatus.Success,
                MicrosoftCsvExtractionStatus.EmptyDocument => Microsoft365ContentExtractionStatus.EmptyDocument,
                MicrosoftCsvExtractionStatus.CorruptedDocument => Microsoft365ContentExtractionStatus.CorruptedDocument,
                MicrosoftCsvExtractionStatus.TooLarge => Microsoft365ContentExtractionStatus.TooLarge,
                _ => throw new ArgumentOutOfRangeException(nameof(result.Status), result.Status, null)
            },
            result.Units.Select(unit => new Microsoft365ExtractedContentUnit(
                Microsoft365ExtractedContentUnitKind.Table,
                unit.Order,
                unit.Text,
                unit.SourcePart)).ToArray(),
            []);
    }
}
