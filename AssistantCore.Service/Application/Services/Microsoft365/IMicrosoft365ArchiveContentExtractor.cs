using AssistantCore.Service.Application.Models.Microsoft365.ContentExtraction;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public interface IMicrosoft365ArchiveContentExtractor
{
    bool CanExtract(string fileName, string? mimeType);

    Task<Microsoft365ContentExtractionResult> ExtractAsync(
        Microsoft365ContentExtractionRequest request,
        CancellationToken cancellationToken);
}
