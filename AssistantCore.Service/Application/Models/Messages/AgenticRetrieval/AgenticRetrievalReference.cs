namespace AssistantCore.Service.Application.Models.Messages.AgenticRetrieval;

public sealed record AgenticRetrievalReference(
    string ReferenceId,
    string DocumentKey,
    string Title,
    string Content,
    string? SiteId,
    string? DriveId,
    string? DriveItemId,
    string? Url,
    DateTimeOffset? ModifiedAt,
    double? RelevanceScore,
    string? ArchivePath = null);
