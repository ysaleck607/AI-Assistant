using System.Globalization;
using System.Text.Json;
using AssistantCore.Repository.Abstractions;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Services.Incidents;

namespace AssistantCore.Service.Middleware;

public sealed class ExceptionMiddleware(
    RequestDelegate next,
    ILogger<ExceptionMiddleware> logger,
    IHostEnvironment environment)
{
    // IOperationalIncidentReporter est scoped : il doit etre resolu par requete via un
    // parametre de InvokeAsync (et non du constructeur), puisque ce middleware n'est
    // instancie qu'une seule fois par app.UseMiddleware<ExceptionMiddleware>().
    public async Task InvokeAsync(HttpContext context, IOperationalIncidentReporter incidentReporter)
    {
        context.Response.Headers["X-Correlation-Id"] = context.TraceIdentifier;

        try
        {
            await next(context);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(exception, "Authentication failed while processing the request.");

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";

            var response = new ExceptionResponse(
                exception.Message,
                environment.IsDevelopment() ? exception.Message : null);

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
        catch (ForbiddenException exception)
        {
            logger.LogWarning(exception, "Access denied while processing the request.");

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";

            var response = new ExceptionResponse(
                exception.Message,
                environment.IsDevelopment() ? exception.Message : null,
                (exception as IErrorCodeException)?.ErrorCode);

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
        catch (BadRequestException exception)
        {
            logger.LogWarning(exception, "Invalid request while processing the request.");

            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";

            var response = new ExceptionResponse(
                exception.Message,
                environment.IsDevelopment() ? exception.Message : null,
                (exception as IErrorCodeException)?.ErrorCode);

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
        catch (ConflictException exception)
        {
            logger.LogWarning(exception, "A conflicting resource prevented the request from completing.");

            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/json";

            var response = new ExceptionResponse(
                exception.Message,
                environment.IsDevelopment() ? exception.Message : null,
                exception.ErrorCode);

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
        catch (NotFoundException exception)
        {
            logger.LogWarning(exception, "Requested resource was not found.");

            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "application/json";

            var response = new ExceptionResponse(
                exception.Message,
                environment.IsDevelopment() ? exception.Message : null);

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
        catch (RequestRateLimitExceededException exception)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "application/json";
            context.Response.Headers["Retry-After"] =
                exception.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);

            var response = new ExceptionResponse(
                exception.Message,
                environment.IsDevelopment() ? exception.Message : null,
                exception.ErrorCode,
                new { exception.RetryAfterSeconds });

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("The client cancelled the request.");
        }
        catch (ExternalSourcesUnavailableException exception)
        {
            logger.LogWarning(
                "External sources required by the orchestration are unavailable. Code: {TechnicalCode}.",
                ExternalSourcesUnavailableException.TechnicalCode);

            await ReportIncidentAsync(context, incidentReporter, exception);

            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            context.Response.ContentType = "application/json";

            var response = new ExceptionResponse(
                exception.Message,
                environment.IsDevelopment() ? exception.Message : null);

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
        catch (Microsoft365ExternalException exception)
        {
            logger.LogWarning(exception, "Microsoft 365 consent provider is unavailable.");

            await ReportIncidentAsync(context, incidentReporter, exception);

            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            context.Response.ContentType = "application/json";

            var response = new ExceptionResponse(
                "Microsoft 365 consent could not be completed.",
                environment.IsDevelopment() ? exception.Message : null);

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
        catch (AzureAiSearchUnavailableException exception)
        {
            logger.LogWarning(exception, "Azure AI Search request failed.");

            await ReportIncidentAsync(context, incidentReporter, exception);

            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            context.Response.ContentType = "application/json";

            var response = new ExceptionResponse(
                "Azure AI Search request failed.",
                environment.IsDevelopment() ? exception.Message : null);

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
        catch (AiProviderException exception)
        {
            var providerStatusCode = exception is AiProviderUnavailableException unavailable
                ? unavailable.ProviderStatusCode
                : null;

            logger.LogWarning(
                "AI provider request failed. Provider: {ProviderName}; code: {TechnicalCode}; provider status: {ProviderStatusCode}.",
                exception.ProviderName,
                exception.TechnicalCode,
                providerStatusCode);

            await ReportIncidentAsync(context, incidentReporter, exception);

            context.Response.StatusCode = exception switch
            {
                AiProviderTimeoutException => StatusCodes.Status504GatewayTimeout,
                AiProviderLimitException => StatusCodes.Status429TooManyRequests,
                AiProviderUnavailableException or AiProviderInvalidResponseException =>
                    StatusCodes.Status502BadGateway,
                _ => StatusCodes.Status500InternalServerError
            };
            context.Response.ContentType = "application/json";

            var response = new ExceptionResponse(
                exception.Message,
                environment.IsDevelopment() ? exception.Message : null,
                exception is AiProviderLimitException ? "ai_provider_rate_limited" : null);

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "An unhandled exception occurred while processing the request.");

            await ReportIncidentAsync(context, incidentReporter, exception);

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";

            var response = new ExceptionResponse(
                "An unexpected error occurred.",
                environment.IsDevelopment() ? exception.Message : null);

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
    }

    private static Task ReportIncidentAsync(
        HttpContext context,
        IOperationalIncidentReporter incidentReporter,
        Exception exception)
    {
        return incidentReporter.ReportAsync(
            new OperationalIncidentReport(
                exception,
                context.TraceIdentifier,
                OrganizationId: TryGetOrganizationIdFromRoute(context)),
            context.RequestAborted);
    }

    // Seule source fiable et generique disponible au niveau du middleware : le parametre de
    // route {organizationId} utilise par les endpoints backoffice. Les endpoints qui ne
    // l'exposent pas produisent un incident non rattache a une organisation (OrganizationId
    // nullable, voir OperationalIncident) plutot qu'une resolution fragile via les claims.
    private static Guid? TryGetOrganizationIdFromRoute(HttpContext context)
    {
        return context.Request.RouteValues.TryGetValue("organizationId", out var value)
            && Guid.TryParse(value?.ToString(), out var organizationId)
            ? organizationId
            : null;
    }

    private sealed record ExceptionResponse(
        string Message,
        string? Detail,
        string? Code = null,
        object? Metadata = null);
}
