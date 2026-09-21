using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Messages;
using AssistantCore.Service.Application.Services.Incidents;
using AssistantCore.Service.Application.Services.RateLimiting;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.RateLimiting;

public sealed class MessageRateLimitServiceTests
{
    [Theory, AutoDomainData]
    public async Task Given_OrganizationAndMemberAreBelowTheirLimits_When_EnsureAllowedAsync_Then_AcquiresBothRulesTogether(
        MessageUserContext userContext)
    {
        // Given
        var store = new StubRateLimitStore(new RateLimitAcquireResult(true, TimeSpan.Zero));
        var service = CreateService(store);

        // When
        await service.EnsureAllowedAsync(userContext, CancellationToken.None);

        // Then
        Assert.Equal(2, store.ReceivedRules.Count);
        Assert.Contains(
            store.ReceivedRules,
            rule => rule.Key == $"rate:member:{userContext.Organization.Id:N}:{userContext.Member.Id:N}:messages"
                && rule.Limit == 10
                && rule.Window == TimeSpan.FromMinutes(1));
        Assert.Contains(
            store.ReceivedRules,
            rule => rule.Key == $"rate:organization:{userContext.Organization.Id:N}:messages"
                && rule.Limit == 100
                && rule.Window == TimeSpan.FromMinutes(1));
        Assert.Equal(1, store.CallCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnyRateLimitIsReached_When_EnsureAllowedAsync_Then_ThrowsWithRoundedRetryAfter(
        MessageUserContext userContext)
    {
        // Given
        var store = new StubRateLimitStore(
            new RateLimitAcquireResult(false, TimeSpan.FromSeconds(12.2)));
        var service = CreateService(store);

        // When
        var exception = await Assert.ThrowsAsync<RequestRateLimitExceededException>(() =>
            service.EnsureAllowedAsync(userContext, CancellationToken.None));

        // Then
        Assert.Equal(RequestRateLimitExceededException.Code, exception.ErrorCode);
        Assert.Equal(13, exception.RetryAfterSeconds);
        Assert.Equal(1, store.CallCount);
        Assert.Equal(2, store.ReceivedRules.Count);
    }

    [Theory, AutoDomainData]
    public async Task Given_RateLimitReachedAndAlertGateOpen_When_EnsureAllowedAsync_Then_ReportsACapacityIncident(
        MessageUserContext userContext)
    {
        // Given
        var store = new StubRateLimitStore(new RateLimitAcquireResult(false, TimeSpan.FromSeconds(5)));
        var reporter = new RecordingOperationalIncidentReporter();
        var service = CreateService(store, alertGateResult: true, reporter: reporter);

        // When
        await Assert.ThrowsAsync<RequestRateLimitExceededException>(() =>
            service.EnsureAllowedAsync(userContext, CancellationToken.None));

        // Then
        var report = Assert.Single(reporter.ReceivedReports);
        Assert.IsType<OrganizationCapacityAlertException>(report.Exception);
        Assert.Equal(userContext.Organization.Id, report.OrganizationId);
        Assert.Equal(userContext.Member.Id, report.OrganizationMemberId);
    }

    [Theory, AutoDomainData]
    public async Task Given_RateLimitReachedButAlertGateClosed_When_EnsureAllowedAsync_Then_DoesNotReportAnIncident(
        MessageUserContext userContext)
    {
        // Given : anti-spam actif - une alerte a deja ete envoyee recemment pour cette organisation.
        var store = new StubRateLimitStore(new RateLimitAcquireResult(false, TimeSpan.FromSeconds(5)));
        var reporter = new RecordingOperationalIncidentReporter();
        var service = CreateService(store, alertGateResult: false, reporter: reporter);

        // When
        await Assert.ThrowsAsync<RequestRateLimitExceededException>(() =>
            service.EnsureAllowedAsync(userContext, CancellationToken.None));

        // Then
        Assert.Empty(reporter.ReceivedReports);
    }

    private static MessageRateLimitService CreateService(
        IRateLimitStore store,
        bool alertGateResult = true,
        IOperationalIncidentReporter? reporter = null) =>
        new(
            store,
            Options.Create(new RateLimitingOptions
            {
                MemberMessagesPerMinute = 10,
                OrganizationMessagesPerMinute = 100
            }),
            new StubOrganizationCapacityAlertGate(alertGateResult),
            reporter ?? new RecordingOperationalIncidentReporter());

    private sealed class StubRateLimitStore(RateLimitAcquireResult result) : IRateLimitStore
    {
        public int CallCount { get; private set; }

        public IReadOnlyCollection<RateLimitRule> ReceivedRules { get; private set; } = [];

        public Task<RateLimitAcquireResult> TryAcquireAsync(
            IReadOnlyCollection<RateLimitRule> rules,
            CancellationToken cancellationToken)
        {
            CallCount++;
            ReceivedRules = rules;
            return Task.FromResult(result);
        }
    }

    private sealed class StubOrganizationCapacityAlertGate(bool result) : IOrganizationCapacityAlertGate
    {
        public bool TryAcquire(Guid organizationId) => result;
    }

    private sealed class RecordingOperationalIncidentReporter : IOperationalIncidentReporter
    {
        public List<OperationalIncidentReport> ReceivedReports { get; } = [];

        public Task ReportAsync(OperationalIncidentReport report, CancellationToken cancellationToken = default)
        {
            ReceivedReports.Add(report);
            return Task.CompletedTask;
        }
    }
}
