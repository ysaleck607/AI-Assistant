namespace AssistantCore.ExternalServices.Entities.Azure;

public sealed record AzureAiSearchPassageDocument(
    string ChunkId,
    string OrganizationId,
    string Title,
    string Content,
    IReadOnlyCollection<string> AllowedUserIds,
    IReadOnlyCollection<string> AllowedGroupIds,
    IReadOnlyCollection<string> AllowedSharePointGroupIds,
    bool HasAnonymousLink,
    bool HasOrganizationLink,
    string AclFingerprint,
    bool IsAvailable,
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
