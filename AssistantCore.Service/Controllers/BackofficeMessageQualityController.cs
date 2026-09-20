using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Commands.GetBackofficeMessageWarnings;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Infrastructure.Authentication.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace AssistantCore.Service.Controllers;

/// <summary>
/// Resume des reponses signalees par l'agent (#3) : s'appuie sur MessageWarning, deja
/// enregistre par MessageProcessingLifecycleService mais jamais expose jusqu'ici. Ne
/// retourne jamais le contenu integral d'un message, seulement le texte de l'avertissement.
/// </summary>
[ApiController]
[Authorize(Policy = ApiAuthorizationPolicies.ManagementAdmin)]
[Route("api/backoffice/message-warnings")]
public sealed class BackofficeMessageQualityController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(Summary = "Lister les reponses signalees par l'agent pour le backoffice")]
    [SwaggerResponse(StatusCodes.Status200OK, "Warnings returned.", typeof(BackofficeMessageWarningListResponse))]
    public async Task<ActionResult<BackofficeMessageWarningListResponse>> GetWarnings(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] Guid? organizationId = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var response = await dispatcher.SendAsync(
            new GetBackofficeMessageWarningsQuery(page, pageSize, organizationId, from, to),
            cancellationToken);

        return Ok(response);
    }
}
