using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Commands.GetBackofficeIncidents;

public sealed record GetBackofficeIncidentsQuery(
    int Page,
    int PageSize,
    Guid? OrganizationId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    OperationalIncidentSubsystem? Subsystem,
    OperationalIncidentSeverity? Severity,
    string? CorrelationId) : IRequest<BackofficeIncidentListResponse>;
