using System.Text.Json;
using AssistantCore.Repository.Abstractions;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Middleware;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssistantCore.Service.Tests.Middleware;

public sealed class ExceptionMiddlewareTests
{
    [Theory]
    [InlineData("unauthorized", StatusCodes.Status401Unauthorized)]
    [InlineData("forbidden", StatusCodes.Status403Forbidden)]
    [InlineData("bad-request", StatusCodes.Status400BadRequest)]
    [InlineData("conflict", StatusCodes.Status409Conflict)]
    [InlineData("not-found", StatusCodes.Status404NotFound)]
    public async Task Given_AKnownException_When_InvokingMiddleware_Then_ReturnsExpectedJsonError(
        string exceptionType,
        int expectedStatusCode)
    {
        // Given
        const string message = "Expected failure.";
        var exception = CreateKnownException(exceptionType, message);
        var context = CreateHttpContext();
        var middleware = CreateMiddleware(
            _ => Task.FromException(exception),
            Environments.Development);

        // When
        await middleware.InvokeAsync(context);

        // Then
        using var response = await ReadResponse(context);
        Assert.Equal(expectedStatusCode, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);
        Assert.Equal(message, response.RootElement.GetProperty("Message").GetString());
        Assert.Equal(message, response.RootElement.GetProperty("Detail").GetString());
    }

    [Fact]
    public async Task Given_ATenantAdmissionException_When_InvokingMiddleware_Then_ReturnsForbiddenWithCode()
    {
        // Given
        const string message = "A tenant administrator must finish the Microsoft 365 setup.";
        var exception = new TenantAdmissionException(message, TenantAdmissionException.TenantAdminRequired);
        var context = CreateHttpContext();
        var middleware = CreateMiddleware(
            _ => Task.FromException(exception),
            Environments.Development);

        // When
        await middleware.InvokeAsync(context);

        // Then
        using var response = await ReadResponse(context);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal(message, response.RootElement.GetProperty("Message").GetString());
        Assert.Equal(
            TenantAdmissionException.TenantAdminRequired,
            response.RootElement.GetProperty("Code").GetString());
    }

    [Fact]
    public async Task Given_AMicrosoft365ConsentException_When_InvokingMiddleware_Then_ReturnsBadRequestWithCode()
    {
        // Given
        const string message = "Microsoft 365 required permissions are missing.";
        var exception = new Microsoft365ConsentException(
            message,
            Microsoft365ConsentException.MissingRequiredPermissions);
        var context = CreateHttpContext();
        var middleware = CreateMiddleware(
            _ => Task.FromException(exception),
            Environments.Development);

        // When
        await middleware.InvokeAsync(context);

        // Then
        using var response = await ReadResponse(context);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(message, response.RootElement.GetProperty("Message").GetString());
        Assert.Equal(
            Microsoft365ConsentException.MissingRequiredPermissions,
            response.RootElement.GetProperty("Code").GetString());
    }

    [Fact]
    public async Task Given_APlainBadRequestException_When_InvokingMiddleware_Then_ReturnsNullCode()
    {
        // Given
        var exception = new BadRequestException("Invalid request.");
        var context = CreateHttpContext();
        var middleware = CreateMiddleware(
            _ => Task.FromException(exception),
            Environments.Development);

        // When
        await middleware.InvokeAsync(context);

        // Then
        using var response = await ReadResponse(context);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(JsonValueKind.Null, response.RootElement.GetProperty("Code").ValueKind);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnExhaustedOrganizationQuota_When_InvokeAsync_Then_Returns429WithPeriodEndMetadata(
        DateTimeOffset periodEndsAt)
    {
        // Given
        var exception = new OrganizationTokenQuotaExceededException(periodEndsAt);
        var context = CreateHttpContext();
        var middleware = CreateMiddleware(
            _ => Task.FromException(exception),
            Environments.Production);

        // When
        await middleware.InvokeAsync(context);

        // Then
        using var response = await ReadResponse(context);
        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal(exception.ErrorCode, response.RootElement.GetProperty("Code").GetString());
        Assert.Equal(
            periodEndsAt,
            response.RootElement.GetProperty("Metadata").GetProperty("PeriodEndsAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, response.RootElement.GetProperty("Detail").ValueKind);
    }

    [Fact]
    public async Task Given_APlainForbiddenException_When_InvokingMiddleware_Then_ReturnsNullCode()
    {
        // Given
        var exception = new ForbiddenException("Organization access denied.");
        var context = CreateHttpContext();
        var middleware = CreateMiddleware(
            _ => Task.FromException(exception),
            Environments.Development);

        // When
        await middleware.InvokeAsync(context);

        // Then
        using var response = await ReadResponse(context);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal(JsonValueKind.Null, response.RootElement.GetProperty("Code").ValueKind);
    }

    [Fact]
    public async Task Given_AnUnexpectedExceptionInDevelopment_When_InvokingMiddleware_Then_ReturnsGenericErrorWithDetail()
    {
        // Given
        var context = CreateHttpContext();
        var middleware = CreateMiddleware(
            _ => Task.FromException(new InvalidOperationException("Database unavailable.")),
            Environments.Development);

        // When
        await middleware.InvokeAsync(context);

        // Then
        using var response = await ReadResponse(context);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("An unexpected error occurred.", response.RootElement.GetProperty("Message").GetString());
        Assert.Equal("Database unavailable.", response.RootElement.GetProperty("Detail").GetString());
    }

    [Theory]
    [InlineData("timeout", StatusCodes.Status504GatewayTimeout)]
    [InlineData("limit", StatusCodes.Status429TooManyRequests)]
    [InlineData("unavailable", StatusCodes.Status502BadGateway)]
    [InlineData("invalid-response", StatusCodes.Status502BadGateway)]
    public async Task Given_AProviderException_When_InvokeAsync_Then_ReturnsTheControlledStatus(
        string failureType,
        int expectedStatusCode)
    {
        // Given
        var exception = CreateProviderException(failureType);
        var context = CreateHttpContext();
        var middleware = CreateMiddleware(
            _ => Task.FromException(exception),
            Environments.Development);

        // When
        await middleware.InvokeAsync(context);

        // Then
        using var response = await ReadResponse(context);
        Assert.Equal(expectedStatusCode, context.Response.StatusCode);
        Assert.Equal(exception.Message, response.RootElement.GetProperty("Message").GetString());
        Assert.DoesNotContain("ApiKey", response.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory, AutoDomainData]
    public async Task Given_AllExternalSourcesUnavailable_When_InvokeAsync_Then_ReturnsBadGateway(
        ExternalSourcesUnavailableException exception)
    {
        // Given
        var context = CreateHttpContext();
        var middleware = CreateMiddleware(
            _ => Task.FromException(exception),
            Environments.Development);

        // When
        await middleware.InvokeAsync(context);

        // Then
        using var response = await ReadResponse(context);
        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
        Assert.Equal(exception.Message, response.RootElement.GetProperty("Message").GetString());
    }

    [Fact]
    public async Task Given_AClientCancellation_When_InvokeAsync_Then_DoesNotWriteAnErrorResponse()
    {
        // Given
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();
        var context = CreateHttpContext();
        context.RequestAborted = cancellationSource.Token;
        var middleware = CreateMiddleware(
            _ => Task.FromCanceled(cancellationSource.Token),
            Environments.Production);

        // When
        await middleware.InvokeAsync(context);

        // Then
        Assert.Equal(0, context.Response.Body.Length);
        Assert.Null(context.Response.ContentType);
    }

    [Fact]
    public async Task Given_AnUnexpectedExceptionInProduction_When_InvokingMiddleware_Then_HidesTechnicalDetail()
    {
        // Given
        var context = CreateHttpContext();
        var middleware = CreateMiddleware(
            _ => Task.FromException(new InvalidOperationException("Sensitive detail.")),
            Environments.Production);

        // When
        await middleware.InvokeAsync(context);

        // Then
        using var response = await ReadResponse(context);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal(JsonValueKind.Null, response.RootElement.GetProperty("Detail").ValueKind);
    }

    [Fact]
    public async Task Given_NoException_When_InvokingMiddleware_Then_ContinuesThePipeline()
    {
        // Given
        var nextWasCalled = false;
        var context = CreateHttpContext();
        var middleware = CreateMiddleware(
            httpContext =>
            {
                nextWasCalled = true;
                httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            },
            Environments.Production);

        // When
        await middleware.InvokeAsync(context);

        // Then
        Assert.True(nextWasCalled);
        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    private static ExceptionMiddleware CreateMiddleware(
        RequestDelegate next,
        string environmentName) =>
        new(
            next,
            NullLogger<ExceptionMiddleware>.Instance,
            new StubHostEnvironment { EnvironmentName = environmentName });

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<JsonDocument> ReadResponse(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }

    private static Exception CreateKnownException(string exceptionType, string message) =>
        exceptionType switch
        {
            "unauthorized" => new UnauthorizedAccessException(message),
            "forbidden" => new ForbiddenException(message),
            "bad-request" => new BadRequestException(message),
            "conflict" => new ConflictException(message),
            "not-found" => new NotFoundException(message),
            _ => throw new ArgumentOutOfRangeException(nameof(exceptionType), exceptionType, null)
        };

    private static AiProviderException CreateProviderException(string failureType) =>
        failureType switch
        {
            "timeout" => new AiProviderTimeoutException("OpenAI"),
            "limit" => new AiProviderLimitException("OpenAI"),
            "unavailable" => new AiProviderUnavailableException("OpenAI"),
            "invalid-response" => new AiProviderInvalidResponseException("OpenAI"),
            _ => throw new ArgumentOutOfRangeException(nameof(failureType), failureType, null)
        };

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "AssistantCore.Service.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
