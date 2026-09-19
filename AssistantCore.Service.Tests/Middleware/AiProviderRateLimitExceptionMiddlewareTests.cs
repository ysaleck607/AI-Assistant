using System.Text.Json;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Middleware;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssistantCore.Service.Tests.Middleware;

public sealed class AiProviderRateLimitExceptionMiddlewareTests
{
    [Theory, AutoDomainData]
    public async Task Given_AProviderRateLimit_When_InvokeAsync_Then_ReturnsStable429Code(
        string providerName)
    {
        // Given
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var exception = new AiProviderLimitException(providerName);
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
        Assert.Equal(
            "ai_provider_rate_limited",
            response.RootElement.GetProperty("Code").GetString());
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "AssistantCore.Service.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
