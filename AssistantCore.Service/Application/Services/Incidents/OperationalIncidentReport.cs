using AssistantCore.Repository.Domain.Enums;

namespace AssistantCore.Service.Application.Services.Incidents;

public sealed record OperationalIncidentReport(
    Exception Exception,
    string CorrelationId,
    Guid? OrganizationId = null,
    Guid? OrganizationMemberId = null,
    string? RelatedResourceType = null,
    Guid? RelatedResourceId = null,
    OperationalIncidentSubsystem? SubsystemOverride = null);
