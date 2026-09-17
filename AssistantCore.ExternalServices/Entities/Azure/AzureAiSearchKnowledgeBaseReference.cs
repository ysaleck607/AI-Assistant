namespace AssistantCore.ExternalServices.Entities.Azure;

public sealed record AzureAiSearchKnowledgeBaseReference(
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
