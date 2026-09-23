using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Commands.GetBackofficeUsage;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Infrastructure.Authentication.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace AssistantCore.Service.Controllers;

/// <summary>
/// Frequence d'utilisation du SaaS pour le backoffice (demande de Giovani, 2026-09-22).
/// Un "utilisateur actif" est un membre d'organisation ayant envoye au moins un message
/// (jamais une reponse assistant) dans la periode. Aucun contenu de message n'est jamais
/// expose ici, uniquement des compteurs.
/// </summary>
[ApiController]
[Authorize(Policy = ApiAuthorizationPolicies.ManagementAdmin)]
[Route("api/backoffice/usage")]
public sealed class BackofficeUsageController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("summary")]
    [SwaggerOperation(Summary = "Resume d'utilisation (DAU, WAU, MAU, requetes du jour) pour le backoffice")]
    [SwaggerResponse(StatusCodes.Status200OK, "Summary returned.", typeof(BackofficeUsageSummaryDto))]
    public async Task<ActionResult<BackofficeUsageSummaryDto>> GetSummary(
        CancellationToken cancellationToken)
    {
        var response = await dispatcher.SendAsync(new GetBackofficeUsageSummaryQuery(), cancellationToken);
        return Ok(response);
    }

    [HttpGet("daily")]
    [SwaggerOperation(Summary = "Serie quotidienne (utilisateurs actifs et requetes) pour le backoffice")]
    [SwaggerResponse(StatusCodes.Status200OK, "Daily series returned.", typeof(BackofficeUsageDailySeriesResponse))]
    public async Task<ActionResult<BackofficeUsageDailySeriesResponse>> GetDailySeries(
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default)
    {
        var response = await dispatcher.SendAsync(
            new GetBackofficeUsageDailySeriesQuery(days),
            cancellationToken);
        return Ok(response);
    }

    [HttpGet("by-organization")]
    [SwaggerOperation(Summary = "Utilisation par organisation (par defaut : mois en cours) pour le backoffice")]
    [SwaggerResponse(StatusCodes.Status200OK, "Usage by organization returned.", typeof(BackofficeUsageByOrganizationResponse))]
    public async Task<ActionResult<BackofficeUsageByOrganizationResponse>> GetByOrganization(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var response = await dispatcher.SendAsync(
            new GetBackofficeUsageByOrganizationQuery(from, to),
            cancellationToken);
        return Ok(response);
    }
}
