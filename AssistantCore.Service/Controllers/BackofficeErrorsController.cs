using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Commands.GetBackofficeIncidentDetails;
using AssistantCore.Service.Application.Commands.GetBackofficeIncidents;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Infrastructure.Authentication.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace AssistantCore.Service.Controllers;

/// <summary>
/// Erreurs operationnelles centralisees (#15) reservees aux operateurs Synaptix : jamais
/// [AllowAnonymous], contrairement au gap pre-existant sur BackofficeOrganizationsController.
/// </summary>
[ApiController]
[Authorize(Policy = ApiAuthorizationPolicies.ManagementAdmin)]
[Route("api/backoffice/errors")]
public sealed class BackofficeErrorsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(Summary = "Lister les incidents operationnels pour le backoffice")]
    [SwaggerResponse(StatusCodes.Status200OK, "Incidents returned.", typeof(BackofficeIncidentListResponse))]
    public async Task<ActionResult<BackofficeIncidentListResponse>> GetIncidents(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] Guid? organizationId = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] OperationalIncidentSubsystem? subsystem = null,
        [FromQuery] OperationalIncidentSeverity? severity = null,
        [FromQuery] string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await dispatcher.SendAsync(
            new GetBackofficeIncidentsQuery(
                page,
                pageSize,
                organizationId,
                from,
                to,
                subsystem,
                severity,
                correlationId),
            cancellationToken);

        return Ok(response);
    }

    [HttpGet("{incidentId:guid}")]
    [SwaggerOperation(Summary = "Lire le detail securise d'un incident operationnel")]
    [SwaggerResponse(StatusCodes.Status200OK, "Incident details returned.", typeof(BackofficeIncidentDetailDto))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Incident not found.")]
    public async Task<ActionResult<BackofficeIncidentDetailDto>> GetIncidentDetails(
        [FromRoute] Guid incidentId,
        CancellationToken cancellationToken)
    {
        var response = await dispatcher.SendAsync(
            new GetBackofficeIncidentDetailsQuery(incidentId),
            cancellationToken);

        return Ok(response);
    }
}
