using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Services.Incidents;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssistantCore.Service.Tests.Incidents;

public sealed class OperationalIncidentReporterTests
{
    [Fact]
    public async Task Given_AReportableException_When_Reporting_Then_TheIncidentIsPersistedWithRedactedText()
    {
        // Given
        var repository = new RecordingOperationalIncidentRepository();
        var reporter = CreateReporter(repository);
        var organizationId = Guid.NewGuid();
        var report = new OperationalIncidentReport(
            new AiProviderTimeoutException("Foundry"),
            "corr-1",
            OrganizationId: organizationId);

        // When
        await reporter.ReportAsync(report);

        // Then
        Assert.NotNull(repository.PersistedIncident);
        Assert.Equal("corr-1", repository.PersistedIncident!.CorrelationId);
        Assert.Equal(organizationId, repository.PersistedIncident.OrganizationId);
        Assert.Equal(OperationalIncidentSubsystem.FoundryLlm, repository.PersistedIncident.Subsystem);
        Assert.Equal(SentinelSensitiveDataRedactor.Sentinel, repository.PersistedIncident.Summary);
        Assert.Equal(SentinelSensitiveDataRedactor.Sentinel, repository.PersistedIncident.SafeDetail);
    }

    [Fact]
    public async Task Given_ASubsystemOverride_When_Reporting_Then_TheOverrideWinsOverTheClassifier()
    {
        // Given
        var repository = new RecordingOperationalIncidentRepository();
        var reporter = CreateReporter(repository);
        var report = new OperationalIncidentReport(
            new InvalidOperationException("Worker cycle failed."),
            "corr-2",
            SubsystemOverride: OperationalIncidentSubsystem.WorkerJobs);

        // When
        await reporter.ReportAsync(report);

        // Then
        Assert.Equal(OperationalIncidentSubsystem.WorkerJobs, repository.PersistedIncident!.Subsystem);
    }

    [Fact]
    public async Task Given_APersistenceFailure_When_Reporting_Then_TheFailureIsSwallowedNotRethrown()
    {
        // Given
        var reporter = CreateReporter(new ThrowingOperationalIncidentRepository());
        var report = new OperationalIncidentReport(new InvalidOperationException("Unhandled."), "corr-3");

        // When
        var exception = await Record.ExceptionAsync(() => reporter.ReportAsync(report));

        // Then
        Assert.Null(exception);
    }

    private static OperationalIncidentReporter CreateReporter(IOperationalIncidentRepository repository) =>
        new(
            repository,
            new SentinelSensitiveDataRedactor(),
            TimeProvider.System,
            NullLogger<OperationalIncidentReporter>.Instance);

    private sealed class RecordingOperationalIncidentRepository : IOperationalIncidentRepository
    {
        public OperationalIncident? PersistedIncident { get; private set; }

        public Task PersistAsync(OperationalIncident incident, CancellationToken cancellationToken = default)
        {
            PersistedIncident = incident;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingOperationalIncidentRepository : IOperationalIncidentRepository
    {
        public Task PersistAsync(OperationalIncident incident, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Database unavailable.");
    }

    private sealed class SentinelSensitiveDataRedactor : ISensitiveDataRedactor
    {
        public const string Sentinel = "REDACTED-SENTINEL";

        public string Redact(string? text) => Sentinel;
    }
}
