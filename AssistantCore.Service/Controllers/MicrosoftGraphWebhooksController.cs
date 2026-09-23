using System.Text;
using System.Text.Json;
using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Commands.ReceiveMicrosoftGraphWebhook;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AssistantCore.Service.Controllers;

[ApiController]
[AllowAnonymous]
[Route("webhooks/microsoft-graph")]
[EnableRateLimiting(RateLimitingServiceCollectionExtensions.MicrosoftGraphWebhookPolicyName)]
public sealed class MicrosoftGraphWebhooksController(IDispatcher dispatcher) : ControllerBase
{
    private const int MaximumNotificationCount = 100;
    private const int MaximumValidationTokenLength = 4096;
    private const long MaximumRequestBodyBytes = 256 * 1024;

    [HttpPost]
    [RequestSizeLimit(MaximumRequestBodyBytes)]
    public async Task<IActionResult> ReceiveAsync(
        [FromQuery] string? validationToken,
        CancellationToken cancellationToken)
    {
        if (validationToken is not null)
        {
            if (validationToken.Length == 0 || validationToken.Length > MaximumValidationTokenLength)
            {
                return BadRequest();
            }

            var validationResult = await dispatcher.SendAsync(
                new ReceiveMicrosoftGraphWebhookCommand(validationToken, null),
                cancellationToken);

            return validationResult.ValidationToken is not null
                ? Content(validationResult.ValidationToken, "text/plain", Encoding.UTF8)
                : Accepted();
        }

        if (!Request.HasJsonContentType())
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        MicrosoftGraphNotificationCollection? notifications;
        try
        {
            notifications = await Request.ReadFromJsonAsync<MicrosoftGraphNotificationCollection>(
                cancellationToken);
        }
        catch (JsonException)
        {
            return BadRequest();
        }
        catch (NotSupportedException)
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        if (notifications?.Value is null || notifications.Value.Count == 0)
        {
            return BadRequest();
        }

        if (notifications.Value.Count > MaximumNotificationCount)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var result = await dispatcher.SendAsync(
            new ReceiveMicrosoftGraphWebhookCommand(null, notifications),
            cancellationToken);

        return result.ValidationToken is not null
            ? Content(result.ValidationToken, "text/plain", Encoding.UTF8)
            : Accepted();
    }
}
