using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Services.Incidents;
using AssistantCore.Service.Middleware;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssistantCore.Service.Tests.Middleware;

public sealed class ExceptionMiddlewareIncidentTests
{
    [Theory]
    [InlineData("microsoft365-external", OperationalIncidentSubsystem.MicrosoftGraph)]
    [InlineData("azure-ai-search", OperationalIncidentSubsystem.AzureAiSearch)]
    [InlineData("ai-provider-timeout", OperationalIncidentSubsystem.FoundryLlm)]
    [InlineData("external-sources-unavailable", OperationalIncidentSubsystem.Application)]
    [InlineData("unhandled", OperationalIncidentSubsystem.Application)]
    public async Task Given_AnOperationalFault_When_InvokingMiddleware_Then_AnIncidentIsReported(
        string exceptionType,
        OperationalIncidentSubsystem expectedSubsystem)
    {
        // Given
        var exception = CreateException(exceptionType);
        var reporter = new RecordingIncidentReporter();
        var context = CreateHttpContext();
        var middleware = CreateMiddleware(_ => Task.FromException(exception));

        // When
        await middleware.InvokeAsync(context, reporter);

        // Then
        Assert.Single(reporter.Reports);
        Assert.Same(exception, reporter.Reports[0].Exception);
        Assert.Equal(context.TraceIdentifier, reporter.Reports[0].CorrelationId);

        var (subsystem, _) = OperationalIncidentSubsystemClassifier.Classify(exception);
        Assert.Equal(expectedSubsystem, subsystem);
    }

    [Theory]
    [InlineData("not-found")]
    [InlineData("bad-request")]
    [InlineData("conflict")]
    public async Task Given_ARoutineBusinessOutcome_When_InvokingMiddleware_Then_NoIncidentIsReported(
        string exceptionType)
    {
        // Given
        var exception = CreateException(exceptionType);
        var reporter = new RecordingIncidentReporter();
        var context = CreateHttpContext();
        var middleware = CreateMiddleware(_ => Task.FromException(exception));

        // When
        await middleware.InvokeAsync(context, reporter);

        // Then
        Assert.Empty(reporter.Reports);
    }

    [Fact]
    public async Task Given_AnyResponse_When_InvokingMiddleware_Then_TheCorrelationIdHeaderIsSet()
    {
        // Given
        var reporter = new RecordingIncidentReporter();
        var context = CreateHttpContext();
        var middleware = CreateMiddleware(httpContext =>
        {
            httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });

        // When
        await middleware.InvokeAsync(context, reporter);

        // Then
        Assert.Equal(context.TraceIdentifier, context.Response.Headers["X-Correlation-Id"].ToString());
    }

    private static Exception CreateException(string exceptionType) => exceptionType switch
    {
        "microsoft365-external" => new Microsoft365ExternalException("Consent provider unavailable."),
        "azure-ai-search" => new AzureAiSearchUnavailableException("Search request failed."),
        "ai-provider-timeout" => new AiProviderTimeoutException("Foundry"),
        "external-sources-unavailable" => new ExternalSourcesUnavailableException(),
        "unhandled" => new InvalidOperationException("Unhandled."),
        "not-found" => new NotFoundException("Not found."),
        "bad-request" => new BadRequestException("Invalid request."),
        "conflict" => new ConflictException("Conflict.", "CONFLICT_CODE"),
        _ => throw new ArgumentOutOfRangeException(nameof(exceptionType), exceptionType, null)
    };

    private static ExceptionMiddleware CreateMiddleware(RequestDelegate next) =>
        new(next, NullLogger<ExceptionMiddleware>.Instance, new StubHostEnvironment());

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private sealed class RecordingIncidentReporter : IOperationalIncidentReporter
    {
        public List<OperationalIncidentReport> Reports { get; } = [];

        public Task ReportAsync(
            OperationalIncidentReport report,
            CancellationToken cancellationToken = default)
        {
            Reports.Add(report);
            return Task.CompletedTask;
        }
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "AssistantCore.Service.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
