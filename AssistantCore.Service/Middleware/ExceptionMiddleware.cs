using System.Text.Json;
using AssistantCore.Repository.Abstractions;
using AssistantCore.Service.Application.Exceptions;

namespace AssistantCore.Service.Middleware;

public sealed class ExceptionMiddleware(
    RequestDelegate next,
    ILogger<ExceptionMiddleware> logger,
    IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
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
        catch (OrganizationTokenQuotaExceededException exception)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "application/json";

            var response = new ExceptionResponse(
                exception.Message,
                environment.IsDevelopment() ? exception.Message : null,
                exception.ErrorCode,
                new { exception.PeriodEndsAt });

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

            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            context.Response.ContentType = "application/json";

            var response = new ExceptionResponse(
                "Microsoft 365 consent could not be completed.",
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
                environment.IsDevelopment() ? exception.Message : null);

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "An unhandled exception occurred while processing the request.");

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";

            var response = new ExceptionResponse(
                "An unexpected error occurred.",
                environment.IsDevelopment() ? exception.Message : null);

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
    }

    private sealed record ExceptionResponse(
        string Message,
        string? Detail,
        string? Code = null,
        object? Metadata = null);
}
