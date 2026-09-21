using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Commands.GetBackofficeAudit;

public sealed record GetBackofficeAuditQuery(
    int Page,
    int PageSize,
    Guid? OrganizationId,
    Guid? ActorId,
    string? Action,
    string? Result,
    DateTimeOffset? From,
    DateTimeOffset? To) : IRequest<BackofficeAuditListResponse>;
