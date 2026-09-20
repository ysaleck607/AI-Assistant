using AssistantCore.Repository.Queries;

namespace AssistantCore.Service.Application.Models.Backoffice;

public sealed record BackofficeMessageWarningSummaryDto(
    Guid Id,
    Guid MessageId,
    Guid ConversationId,
    Guid OrganizationId,
    string OrganizationName,
    string Content,
    DateTimeOffset OccurredAt)
{
    public static BackofficeMessageWarningSummaryDto FromData(BackofficeMessageWarningSummaryData data) => new(
        data.Id,
        data.MessageId,
        data.ConversationId,
        data.OrganizationId,
        data.OrganizationName,
        data.Content,
        data.OccurredAt);
}
