using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using AssistantCore.Repository.Queries;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Service.Tests.Repository;

public sealed class OperationalIncidentQueriesTests
{
    [Theory, AutoDomainData]
    public async Task Given_AnOrganizationFilter_When_SearchAsync_Then_OnlyThatOrganizationsIncidentsAreReturned(
        Guid databaseId)
    {
        // Given
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.OperationalIncidents.AddRange(
            CreateIncident(organizationId: organizationA),
            CreateIncident(organizationId: organizationB));
        await dbContext.SaveChangesAsync();
        var queries = new OperationalIncidentQueries(dbContext);

        // When
        var result = await queries.SearchAsync(
            organizationA, null, null, null, null, null, 1, 25);

        // Then
        var item = Assert.Single(result.Items);
        Assert.Equal(organizationA, item.OrganizationId);
    }

    [Theory, AutoDomainData]
    public async Task Given_TwoOrganizationsIncidents_When_SearchAsyncFilteredByOrganizationA_Then_OrganizationBIncidentsAreNeverReturned(
        Guid databaseId)
    {
        // Given : garde d'isolation multi-tenant explicite pour #15.
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.OperationalIncidents.AddRange(
            CreateIncident(organizationId: organizationA),
            CreateIncident(organizationId: organizationB),
            CreateIncident(organizationId: organizationB));
        await dbContext.SaveChangesAsync();
        var queries = new OperationalIncidentQueries(dbContext);

        // When
        var result = await queries.SearchAsync(
            organizationA, null, null, null, null, null, 1, 25);

        // Then
        Assert.All(result.Items, item => Assert.Equal(organizationA, item.OrganizationId));
        Assert.DoesNotContain(result.Items, item => item.OrganizationId == organizationB);
    }

    [Theory, AutoDomainData]
    public async Task Given_ASubsystemFilter_When_SearchAsync_Then_OnlyMatchingSubsystemIsReturned(
        Guid databaseId)
    {
        // Given
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.OperationalIncidents.AddRange(
            CreateIncident(subsystem: OperationalIncidentSubsystem.FoundryLlm),
            CreateIncident(subsystem: OperationalIncidentSubsystem.AzureAiSearch));
        await dbContext.SaveChangesAsync();
        var queries = new OperationalIncidentQueries(dbContext);

        // When
        var result = await queries.SearchAsync(
            null, null, null, OperationalIncidentSubsystem.FoundryLlm, null, null, 1, 25);

        // Then
        var item = Assert.Single(result.Items);
        Assert.Equal(OperationalIncidentSubsystem.FoundryLlm, item.Subsystem);
    }

    [Theory, AutoDomainData]
    public async Task Given_ASeverityFilter_When_SearchAsync_Then_OnlyMatchingSeverityIsReturned(
        Guid databaseId)
    {
        // Given
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.OperationalIncidents.AddRange(
            CreateIncident(severity: OperationalIncidentSeverity.Critical),
            CreateIncident(severity: OperationalIncidentSeverity.Warning));
        await dbContext.SaveChangesAsync();
        var queries = new OperationalIncidentQueries(dbContext);

        // When
        var result = await queries.SearchAsync(
            null, null, null, null, OperationalIncidentSeverity.Critical, null, 1, 25);

        // Then
        var item = Assert.Single(result.Items);
        Assert.Equal(OperationalIncidentSeverity.Critical, item.Severity);
    }

    [Theory, AutoDomainData]
    public async Task Given_ACorrelationIdFilter_When_SearchAsync_Then_OnlyTheExactMatchIsReturned(
        Guid databaseId)
    {
        // Given
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.OperationalIncidents.AddRange(
            CreateIncident(correlationId: "corr-exact"),
            CreateIncident(correlationId: "corr-other"));
        await dbContext.SaveChangesAsync();
        var queries = new OperationalIncidentQueries(dbContext);

        // When
        var result = await queries.SearchAsync(
            null, null, null, null, null, "corr-exact", 1, 25);

        // Then
        var item = Assert.Single(result.Items);
        Assert.Equal("corr-exact", item.CorrelationId);
    }

    [Theory, AutoDomainData]
    public async Task Given_APeriodFilter_When_SearchAsync_Then_OnlyIncidentsWithinThePeriodAreReturned(
        Guid databaseId)
    {
        // Given
        var inRange = DateTimeOffset.Parse("2026-09-15T12:00:00Z");
        var beforeRange = DateTimeOffset.Parse("2026-09-01T00:00:00Z");
        var afterRange = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.OperationalIncidents.AddRange(
            CreateIncident(occurredAt: inRange),
            CreateIncident(occurredAt: beforeRange),
            CreateIncident(occurredAt: afterRange));
        await dbContext.SaveChangesAsync();
        var queries = new OperationalIncidentQueries(dbContext);

        // When
        var result = await queries.SearchAsync(
            null,
            DateTimeOffset.Parse("2026-09-10T00:00:00Z"),
            DateTimeOffset.Parse("2026-09-20T00:00:00Z"),
            null,
            null,
            null,
            1,
            25);

        // Then
        var item = Assert.Single(result.Items);
        Assert.Equal(inRange, item.OccurredAt);
    }

    [Theory, AutoDomainData]
    public async Task Given_NoIncidentMatches_When_SearchAsync_Then_ReturnsAnEmptyPage(
        Guid databaseId)
    {
        // Given
        await using var dbContext = CreateDbContext(databaseId);
        var queries = new OperationalIncidentQueries(dbContext);

        // When
        var result = await queries.SearchAsync(
            Guid.NewGuid(), null, null, null, null, null, 1, 25);

        // Then
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnExistingIncident_When_GetByIdAsync_Then_ReturnsTheSafeDetail(
        Guid databaseId)
    {
        // Given
        var incident = CreateIncident();
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.OperationalIncidents.Add(incident);
        await dbContext.SaveChangesAsync();
        var queries = new OperationalIncidentQueries(dbContext);

        // When
        var result = await queries.GetByIdAsync(incident.Id);

        // Then
        Assert.NotNull(result);
        Assert.Equal(incident.SafeDetail, result.SafeDetail);
    }

    private static AssistantCoreDbContext CreateDbContext(Guid databaseId)
    {
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(databaseId.ToString())
            .Options;

        return new AssistantCoreDbContext(options);
    }

    private static OperationalIncident CreateIncident(
        Guid? organizationId = null,
        OperationalIncidentSubsystem subsystem = OperationalIncidentSubsystem.Application,
        OperationalIncidentSeverity severity = OperationalIncidentSeverity.Error,
        string? correlationId = null,
        DateTimeOffset? occurredAt = null) => new()
        {
            Id = Guid.NewGuid(),
            OccurredAt = occurredAt ?? DateTimeOffset.Parse("2026-09-15T00:00:00Z"),
            Subsystem = subsystem,
            Severity = severity,
            CorrelationId = correlationId ?? Guid.NewGuid().ToString("N"),
            OrganizationId = organizationId,
            Summary = "Test incident.",
            SafeDetail = "InvalidOperationException: Test incident.",
            Status = OperationalIncidentStatus.Open
        };
}
