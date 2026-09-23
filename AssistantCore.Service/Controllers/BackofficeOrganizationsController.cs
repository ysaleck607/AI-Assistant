using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Commands.GetBackofficeOrganizationDetails;
using AssistantCore.Service.Application.Commands.GetBackofficeOrganizations;
using AssistantCore.Service.Application.Commands.GetBackofficeOrganizationUserDetails;
using AssistantCore.Service.Application.Commands.GetBackofficeOrganizationUsers;
using AssistantCore.Service.Application.Commands.ReevaluateBackofficeUserAccess;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Infrastructure.Authentication.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace AssistantCore.Service.Controllers;

/// <summary>
/// Reservee aux operateurs Synaptix. Anciennement [AllowAnonymous] (gap repere pendant #15).
/// </summary>
[ApiController]
[Authorize(Policy = ApiAuthorizationPolicies.ManagementAdmin)]
[Route("api/backoffice/organizations")]
public sealed class BackofficeOrganizationsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(Summary = "Lister les organisations clientes pour le backoffice")]
    [SwaggerResponse(StatusCodes.Status200OK, "Organizations returned.", typeof(BackofficeOrganizationListResponse))]
    public async Task<ActionResult<BackofficeOrganizationListResponse>> GetOrganizations(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var response = await dispatcher.SendAsync(
            new GetBackofficeOrganizationsQuery(page, pageSize, search),
            cancellationToken);

        return Ok(response);
    }

    [HttpGet("{organizationId:guid}")]
    [SwaggerOperation(Summary = "Lire la fiche générale d'une organisation cliente")]
    [SwaggerResponse(StatusCodes.Status200OK, "Organization details returned.", typeof(BackofficeOrganizationDetailsDto))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Organization not found.")]
    public async Task<ActionResult<BackofficeOrganizationDetailsDto>> GetOrganizationDetails(
        [FromRoute] Guid organizationId,
        CancellationToken cancellationToken)
    {
        var response = await dispatcher.SendAsync(
            new GetBackofficeOrganizationDetailsQuery(organizationId),
            cancellationToken);

        return Ok(response);
    }

    [HttpGet("{organizationId:guid}/users")]
    [SwaggerOperation(Summary = "Lister les utilisateurs et leur diagnostic d'accès")]
    [SwaggerResponse(StatusCodes.Status200OK, "Users returned.", typeof(BackofficeUserListResponse))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Organization not found.")]
    public async Task<ActionResult<BackofficeUserListResponse>> GetUsers(
        [FromRoute] Guid organizationId,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var response = await dispatcher.SendAsync(
            new GetBackofficeOrganizationUsersQuery(organizationId, search),
            cancellationToken);

        return Ok(response);
    }

    [HttpGet("{organizationId:guid}/users/{userId:guid}")]
    [SwaggerOperation(Summary = "Lire le détail et le diagnostic d'accès d'un utilisateur")]
    [SwaggerResponse(StatusCodes.Status200OK, "User details returned.", typeof(BackofficeUserDetailsDto))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Organization or user not found.")]
    public async Task<ActionResult<BackofficeUserDetailsDto>> GetUserDetails(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        var response = await dispatcher.SendAsync(
            new GetBackofficeOrganizationUserDetailsQuery(organizationId, userId),
            cancellationToken);

        return Ok(response);
    }

    [HttpPost("{organizationId:guid}/users/{userId:guid}/reevaluate-access")]
    [SwaggerOperation(Summary = "Réévaluer l'accès d'un utilisateur sans modifier ses données")]
    [SwaggerResponse(StatusCodes.Status200OK, "Access reevaluated.", typeof(BackofficeAccessReevaluationResultDto))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Organization or user not found.")]
    public async Task<ActionResult<BackofficeAccessReevaluationResultDto>> ReevaluateUserAccess(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        var response = await dispatcher.SendAsync(
            new ReevaluateBackofficeUserAccessCommand(organizationId, userId),
            cancellationToken);

        return Ok(response);
    }
}
