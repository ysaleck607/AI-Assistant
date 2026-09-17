namespace AssistantCore.ExternalServices.Entities.Microsoft;

public sealed record MicrosoftMailFolder(
    string Id,
    string DisplayName,
    string? ParentFolderId,
    int ChildFolderCount);
