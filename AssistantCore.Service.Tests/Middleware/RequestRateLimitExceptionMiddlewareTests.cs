using System.Text.Json;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Middleware;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssistantCore.Service.Tests.Middleware;

public sealed class RequestRateLimitExceptionMiddlewareTests
{
    [Theory, AutoDomainData]
    public async Task Given_ARequestRateLimitException_When_InvokeAsync_Then_Returns429WithRetryAfter(
        int generatedRetryAfterSeconds)
    {
        // Given
        var retryAfterSeconds = Math.Max(1, Math.Abs(generatedRetryAfterSeconds % 120));
        var exception = new RequestRateLimitExceededException(retryAfterSeconds);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionMiddleware(
            _ => Task.FromException(exception),
            NullLogger<ExceptionMiddleware>.Instance,
            new StubHostEnvironment());

        // When
        await middleware.InvokeAsync(context);

        // Then
        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal(retryAfterSeconds.ToString(), context.Response.Headers["Retry-After"].ToString());
        Assert.Equal(exception.ErrorCode, response.RootElement.GetProperty("Code").GetString());
        Assert.Equal(
            retryAfterSeconds,
            response.RootElement.GetProperty("Metadata").GetProperty("RetryAfterSeconds").GetInt32());
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "AssistantCore.Service.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
