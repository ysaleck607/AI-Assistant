namespace AssistantCore.Service.Application.Models.Microsoft365;

public sealed record Microsoft365SearchPassage(
    string ChunkId,
    string Title,
    string Content,
    string? SiteId = null,
    string? DriveId = null,
    string? DriveItemId = null,
    string? DocumentVersion = null,
    int ChunkNumber = 0,
    string? Url = null,
    DateTimeOffset? ModifiedAt = null,
    IReadOnlyList<float>? ContentVector = null,
    string SourceType = "sharepoint",
    string? ArchivePath = null);
