namespace AssistantCore.Service.Application.Models.Microsoft365;

public sealed record Microsoft365OutlookMessageDelta(
    string Id,
    string? Subject,
    string? BodyContent,
    string? WebLink,
    DateTimeOffset? CreatedDateTime,
    DateTimeOffset? LastModifiedDateTime,
    DateTimeOffset? ReceivedDateTime,
    bool IsDeleted);

public sealed record Microsoft365OutlookMessageDeltaPage(
    IReadOnlyCollection<Microsoft365OutlookMessageDelta> Items,
    string? DeltaLink);
