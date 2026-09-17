using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Models.Microsoft365.ContentExtraction;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public interface IMicrosoft365DocumentChunkingService
{
    IReadOnlyList<Microsoft365SearchPassage> CreateChunks(
        Guid organizationId,
        Guid sourceId,
        string? siteId,
        string? driveId,
        string driveItemId,
        string documentVersion,
        string title,
        string? url,
        DateTimeOffset? modifiedAt,
        IReadOnlyCollection<Microsoft365ExtractedContentUnit> units);

    IReadOnlyList<Microsoft365SearchPassage> CreateTextChunks(
        Guid organizationId,
        Guid sourceId,
        string itemId,
        string documentVersion,
        string title,
        string? url,
        DateTimeOffset? modifiedAt,
        string content,
        string sourceType);
}
