using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Commands.GetMicrosoft365ReindexStatus;
using AssistantCore.Service.Application.Commands.RequestMicrosoft365Reindex;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Infrastructure.Authentication.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace AssistantCore.Service.Controllers;

/// <summary>
/// Actions Microsoft 365 reservees aux operateurs Synaptix. La politique interne est
/// distincte du role <c>tenantAdmin</c> d'un client : un administrateur client ne peut pas
/// declencher ces actions sur son organisation.
/// </summary>
[ApiController]
[Authorize(Policy = ApiAuthorizationPolicies.ManagementAdmin)]
[Route("api/backoffice/organizations/{organizationId:guid}/microsoft365")]
public sealed class BackofficeMicrosoft365Controller(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost("reindex")]
    [SwaggerOperation(Summary = "Relancer l'indexation SharePoint d'une organisation cliente")]
    [SwaggerResponse(StatusCodes.Status202Accepted, "Reindex accepted.", typeof(BackofficeMicrosoft365ReindexResponse))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "No SharePoint library is enabled for indexing.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Synaptix operator access required.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Organization or active Microsoft 365 connection not found.")]
    [SwaggerResponse(StatusCodes.Status409Conflict, "A reindex is already running for this organization.")]
    public async Task<ActionResult<BackofficeMicrosoft365ReindexResponse>> RequestReindex(
        [FromRoute] Guid organizationId,
        [FromBody] BackofficeMicrosoft365ReindexRequest? request,
        CancellationToken cancellationToken)
    {
        var response = await dispatcher.SendAsync(
            new RequestMicrosoft365ReindexCommand(organizationId, request?.Reason),
            cancellationToken);

        return AcceptedAtAction(
            nameof(GetReindexStatus),
            new { organizationId, operationId = response.OperationId },
            response);
    }

    [HttpGet("reindex/{operationId:guid}")]
    [SwaggerOperation(Summary = "Lire l'etat et la progression d'une reindexation SharePoint")]
    [SwaggerResponse(StatusCodes.Status200OK, "Reindex status returned.", typeof(BackofficeMicrosoft365ReindexStatusResponse))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Synaptix operator access required.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Reindex operation not found.")]
    public async Task<ActionResult<BackofficeMicrosoft365ReindexStatusResponse>> GetReindexStatus(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid operationId,
        CancellationToken cancellationToken)
    {
        var response = await dispatcher.SendAsync(
            new GetMicrosoft365ReindexStatusQuery(organizationId, operationId),
            cancellationToken);

        return Ok(response);
    }
}
