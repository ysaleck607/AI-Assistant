using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using AssistantCore.Repository.Queries;
using AssistantCore.Repository.Repositories.Audit;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Service.Tests.Repository;

public sealed class BackofficeAuditQueriesTests
{
    [Theory, AutoDomainData]
    public async Task Given_AnOrganizationFilter_When_SearchAsync_Then_OnlyThatOrganizationsEntriesAreReturned(
        Guid databaseId)
    {
        // Given
        var organizationA = CreateOrganization("MetalPro");
        var organizationB = CreateOrganization("AtelierNordik");
        var entryA = CreateEntry(organizationA.Id, AdministrativeAuditAction.MemberRoleChanged);
        var entryB = CreateEntry(organizationB.Id, AdministrativeAuditAction.MemberRoleChanged);
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.AddRange(organizationA, organizationB);
        dbContext.AdministrativeAuditEntries.AddRange(entryA, entryB);
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeAuditQueries(dbContext);

        // When
        var result = await queries.SearchAsync(organizationA.Id, null, null, null, null, null, 1, 25);

        // Then
        var item = Assert.Single(result.Items);
        Assert.Equal(organizationA.Id, item.OrganizationId);
        Assert.Equal("MetalPro", item.OrganizationName);
    }

    [Theory, AutoDomainData]
    public async Task Given_TwoOrganizationsEntries_When_SearchAsyncFilteredByOrganizationA_Then_OrganizationBEntriesAreNeverReturned(
        Guid databaseId)
    {
        // Given : garde d'isolation multi-tenant.
        var organizationA = CreateOrganization("MetalPro");
        var organizationB = CreateOrganization("AtelierNordik");
        var entryA = CreateEntry(organizationA.Id, AdministrativeAuditAction.MemberRoleChanged);
        var entryB = CreateEntry(organizationB.Id, AdministrativeAuditAction.MemberRoleChanged);
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.AddRange(organizationA, organizationB);
        dbContext.AdministrativeAuditEntries.AddRange(entryA, entryB);
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeAuditQueries(dbContext);

        // When
        var result = await queries.SearchAsync(organizationA.Id, null, null, null, null, null, 1, 25);

        // Then
        Assert.All(result.Items, item => Assert.Equal(organizationA.Id, item.OrganizationId));
    }

    [Theory, AutoDomainData]
    public async Task Given_AnActorIdFilter_When_SearchAsync_Then_OnlyThatActorsEntriesAreReturned(
        Guid databaseId,
        Guid matchingActorId,
        Guid otherActorId)
    {
        // Given
        var organization = CreateOrganization("MetalPro");
        var matchingEntry = CreateEntry(organization.Id, AdministrativeAuditAction.MemberRoleChanged, actorId: matchingActorId);
        var otherEntry = CreateEntry(organization.Id, AdministrativeAuditAction.MemberRoleChanged, actorId: otherActorId);
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.Add(organization);
        dbContext.AdministrativeAuditEntries.AddRange(matchingEntry, otherEntry);
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeAuditQueries(dbContext);

        // When
        var result = await queries.SearchAsync(null, matchingActorId, null, null, null, null, 1, 25);

        // Then
        var item = Assert.Single(result.Items);
        Assert.Equal(matchingActorId, item.ActorId);
    }

    [Theory, AutoDomainData]
    public async Task Given_AValidActionFilter_When_SearchAsync_Then_OnlyMatchingActionsAreReturned(
        Guid databaseId)
    {
        // Given
        var organization = CreateOrganization("MetalPro");
        var roleChange = CreateEntry(organization.Id, AdministrativeAuditAction.MemberRoleChanged);
        var statusChange = CreateEntry(organization.Id, AdministrativeAuditAction.MemberStatusChanged);
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.Add(organization);
        dbContext.AdministrativeAuditEntries.AddRange(roleChange, statusChange);
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeAuditQueries(dbContext);

        // When
        var result = await queries.SearchAsync(null, null, "MemberRoleChanged", null, null, null, 1, 25);

        // Then
        var item = Assert.Single(result.Items);
        Assert.Equal(AdministrativeAuditAction.MemberRoleChanged, item.Action);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnUnrecognizedActionFilter_When_SearchAsync_Then_ReturnsNoEntries(
        Guid databaseId)
    {
        // Given : un enum ferme - un filtre inconnu ne doit jamais planter ni tout retourner.
        var organization = CreateOrganization("MetalPro");
        var entry = CreateEntry(organization.Id, AdministrativeAuditAction.MemberRoleChanged);
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.Add(organization);
        dbContext.AdministrativeAuditEntries.Add(entry);
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeAuditQueries(dbContext);

        // When
        var result = await queries.SearchAsync(null, null, "NotARealAction", null, null, null, 1, 25);

        // Then
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_AFailedResultFilter_When_SearchAsync_Then_ReturnsNoEntries(
        Guid databaseId)
    {
        // Given : aucune ecriture d'echec n'existe aujourd'hui - "Failed" ne peut jamais matcher.
        var organization = CreateOrganization("MetalPro");
        var entry = CreateEntry(organization.Id, AdministrativeAuditAction.MemberRoleChanged);
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.Add(organization);
        dbContext.AdministrativeAuditEntries.Add(entry);
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeAuditQueries(dbContext);

        // When
        var result = await queries.SearchAsync(null, null, null, "Failed", null, null, 1, 25);

        // Then
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_APeriodFilter_When_SearchAsync_Then_OnlyEntriesWithinThePeriodAreReturned(
        Guid databaseId)
    {
        // Given
        var organization = CreateOrganization("MetalPro");
        var inRangeEntry = CreateEntry(
            organization.Id,
            AdministrativeAuditAction.MemberRoleChanged,
            occurredAt: DateTimeOffset.Parse("2026-09-15T12:00:00Z"));
        var beforeRangeEntry = CreateEntry(
            organization.Id,
            AdministrativeAuditAction.MemberRoleChanged,
            occurredAt: DateTimeOffset.Parse("2026-09-01T00:00:00Z"));
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.Add(organization);
        dbContext.AdministrativeAuditEntries.AddRange(inRangeEntry, beforeRangeEntry);
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeAuditQueries(dbContext);

        // When
        var result = await queries.SearchAsync(
            null,
            null,
            null,
            null,
            DateTimeOffset.Parse("2026-09-10T00:00:00Z"),
            DateTimeOffset.Parse("2026-09-20T00:00:00Z"),
            1,
            25);

        // Then
        var item = Assert.Single(result.Items);
        Assert.Equal(inRangeEntry.Id, item.Id);
    }

    [Theory, AutoDomainData]
    public async Task Given_NoEntryMatches_When_SearchAsync_Then_ReturnsAnEmptyPage(Guid databaseId)
    {
        // Given
        await using var dbContext = CreateDbContext(databaseId);
        var queries = new BackofficeAuditQueries(dbContext);

        // When
        var result = await queries.SearchAsync(Guid.NewGuid(), null, null, null, null, null, 1, 25);

        // Then
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    private static AssistantCoreDbContext CreateDbContext(Guid databaseId)
    {
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(databaseId.ToString())
            .Options;

        return new AssistantCoreDbContext(options);
    }

    private static Organization CreateOrganization(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Domain = $"{name.ToLowerInvariant()}.test",
        IdentityProvider = IdentityProvider.MicrosoftEntraId,
        ExternalTenantId = null,
        Status = RecordStatus.Active,
        CreatedAt = DateTimeOffset.Parse("2026-06-15T00:00:00Z")
    };

    private static AdministrativeAuditEntry CreateEntry(
        Guid organizationId,
        AdministrativeAuditAction action,
        Guid? actorId = null,
        DateTimeOffset? occurredAt = null) =>
        AdministrativeAuditEntryFactory.Create(
            organizationId,
            "ManagementAdmin",
            actorId ?? Guid.NewGuid(),
            action,
            "Member",
            Guid.NewGuid(),
            occurredAt ?? DateTimeOffset.Parse("2026-09-15T00:00:00Z"),
            new Dictionary<string, object?>(),
            new Dictionary<string, object?>(),
            $"audit-{Guid.NewGuid():N}");
}
