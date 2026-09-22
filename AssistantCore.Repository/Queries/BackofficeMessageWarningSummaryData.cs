namespace AssistantCore.Repository.Queries;

public sealed record BackofficeMessageWarningSummaryData(
    Guid Id,
    Guid MessageId,
    Guid ConversationId,
    Guid OrganizationId,
    string OrganizationName,
    string Content,
    DateTimeOffset OccurredAt,
    bool IsContentGap,
    string? QuestionText,
    string? ResponseText);
