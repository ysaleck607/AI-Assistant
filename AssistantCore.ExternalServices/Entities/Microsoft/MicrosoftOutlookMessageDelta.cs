namespace AssistantCore.ExternalServices.Entities.Microsoft;

public sealed record MicrosoftOutlookMessageDelta(
    string Id,
    string? Subject,
    string? BodyContent,
    string? WebLink,
    DateTimeOffset? CreatedDateTime,
    DateTimeOffset? LastModifiedDateTime,
    DateTimeOffset? ReceivedDateTime,
    bool IsDeleted);

public sealed record MicrosoftOutlookMessageDeltaPage(
    IReadOnlyCollection<MicrosoftOutlookMessageDelta> Items,
    string? DeltaLink);
