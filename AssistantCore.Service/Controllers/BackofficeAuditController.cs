using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Commands.GetBackofficeAudit;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Infrastructure.Authentication.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace AssistantCore.Service.Controllers;

/// <summary>
/// Lecture du journal d'audit administratif (#12) : la trace ecrite par
/// AdministrativeAuditEntryFactory n'etait jusqu'ici jamais exposee. Reserve aux
/// operateurs Synaptix, jamais [AllowAnonymous].
/// </summary>
[ApiController]
[Authorize(Policy = ApiAuthorizationPolicies.ManagementAdmin)]
[Route("api/backoffice/audit")]
public sealed class BackofficeAuditController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(Summary = "Lister le journal d'audit administratif pour le backoffice")]
    [SwaggerResponse(StatusCodes.Status200OK, "Audit entries returned.", typeof(BackofficeAuditListResponse))]
    public async Task<ActionResult<BackofficeAuditListResponse>> GetAuditEntries(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] Guid? organizationId = null,
        [FromQuery] string? actor = null,
        [FromQuery] string? action = null,
        [FromQuery] string? result = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        // "actor" ne peut correspondre qu'a ActorId (guid) : un acteur administratif
        // n'est pas un OrganizationMember, aucune jointure email n'existe pour lui.
        var actorId = Guid.TryParse(actor, out var parsedActorId) ? parsedActorId : (Guid?)null;

        var response = await dispatcher.SendAsync(
            new GetBackofficeAuditQuery(
                page,
                pageSize,
                organizationId,
                actorId,
                action,
                result,
                from,
                to),
            cancellationToken);

        return Ok(response);
    }
}
