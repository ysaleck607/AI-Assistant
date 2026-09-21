using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Commands.GetBackofficeMessageWarnings;

public sealed record GetBackofficeMessageWarningsQuery(
    int Page,
    int PageSize,
    Guid? OrganizationId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    bool ContentGapsOnly) : IRequest<BackofficeMessageWarningListResponse>;
