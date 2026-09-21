using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using AssistantCore.Repository.Queries;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Service.Tests.Repository;

public sealed class BackofficeOrganizationQueriesTests
{
    [Theory, AutoDomainData]
    public async Task Given_OrganizationIdSearch_When_SearchOrganizationsAsync_Then_ReturnsMatchingOrganization(
        Guid databaseId)
    {
        // Given
        var organization = CreateOrganization("MetalPro");
        var otherOrganization = CreateOrganization("AtelierNordik");
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.AddRange(organization, otherOrganization);
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeOrganizationQueries(dbContext, new StubEmailBlindIndexHasher());

        // When
        var result = await queries.SearchOrganizationsAsync(
            1,
            25,
            organization.Id.ToString("D"),
            CancellationToken.None);

        // Then
        var item = Assert.Single(result.Items);
        Assert.Equal(organization.Id, item.Id);
        Assert.Equal(1, result.TotalCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_AdminEmailSearch_When_SearchOrganizationsAsync_Then_ReturnsMatchingOrganization(
        Guid databaseId)
    {
        // Given
        var organization = CreateOrganization("MetalPro");
        var otherOrganization = CreateOrganization("AtelierNordik");
        var admin = CreateMember(
            organization.Id,
            "admin@metalpro.test",
            OrganizationRole.Admin,
            RecordStatus.Active);
        var userWithSameDomain = CreateMember(
            otherOrganization.Id,
            "user@metalpro.test",
            OrganizationRole.User,
            RecordStatus.Active);
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.AddRange(organization, otherOrganization);
        dbContext.OrganizationMembers.AddRange(admin, userWithSameDomain);
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeOrganizationQueries(dbContext, new StubEmailBlindIndexHasher());

        // When
        var result = await queries.SearchOrganizationsAsync(
            1,
            25,
            "admin@metalpro.test",
            CancellationToken.None);

        // Then
        var item = Assert.Single(result.Items);
        Assert.Equal(organization.Id, item.Id);
        Assert.Equal(1, result.TotalCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_APartialEmailSearch_When_SearchOrganizationsAsync_Then_DoesNotThrowAndFindsNoMatch(
        Guid databaseId)
    {
        // Given : regression - Email est chiffre au repos, un Contains() SQL dessus levait
        // une SqlException ("invalid escape character") sur toute recherche non vide. La
        // recherche par email n'accepte plus qu'une correspondance exacte (index aveugle).
        var organization = CreateOrganization("MetalPro");
        var admin = CreateMember(
            organization.Id,
            "admin@metalpro.test",
            OrganizationRole.Admin,
            RecordStatus.Active);
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.Add(organization);
        dbContext.OrganizationMembers.Add(admin);
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeOrganizationQueries(dbContext, new StubEmailBlindIndexHasher());

        // When
        var result = await queries.SearchOrganizationsAsync(
            1,
            25,
            "admin@metal",
            CancellationToken.None);

        // Then
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_MicrosoftTenantSearch_When_SearchOrganizationsAsync_Then_ReturnsMatchingOrganization(
        Guid databaseId)
    {
        // Given
        var organization = CreateOrganization("MetalPro");
        var otherOrganization = CreateOrganization("AtelierNordik");
        var connection = new Microsoft365Connection
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            OrganizationConnectorId = Guid.NewGuid(),
            TenantId = "tenant-metal",
            Status = Microsoft365ConnectionStatus.Active,
            CreatedAt = DateTimeOffset.Parse("2026-09-10T19:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-09-10T20:00:00Z")
        };
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.AddRange(organization, otherOrganization);
        dbContext.Microsoft365Connections.Add(connection);
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeOrganizationQueries(dbContext, new StubEmailBlindIndexHasher());

        // When
        var result = await queries.SearchOrganizationsAsync(
            1,
            25,
            "tenant-metal",
            CancellationToken.None);

        // Then
        var item = Assert.Single(result.Items);
        Assert.Equal(organization.Id, item.Id);
        Assert.Equal("tenant-metal", item.TenantId);
        Assert.Equal(1, result.TotalCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_OrganizationData_When_GetOrganizationDetailsAsync_Then_ReturnsAggregatedDetails(
        Guid databaseId)
    {
        // Given
        var organization = CreateOrganization("MetalPro");
        var connector = new OrganizationConnector
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            Type = ConnectorType.Microsoft365,
            Status = RecordStatus.Active,
            IsConfigured = true
        };
        var connection = new Microsoft365Connection
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            OrganizationConnectorId = connector.Id,
            TenantId = "tenant-metal",
            Status = Microsoft365ConnectionStatus.Active,
            ConsentValidatedAt = DateTimeOffset.Parse("2026-09-10T20:00:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-09-10T19:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-09-10T20:00:00Z")
        };
        var site = CreateSite(organization.Id, connector.Id, connection.Id);
        var sharePointDrive = CreateSharePointDrive(
            organization.Id,
            connector.Id,
            connection.Id);
        var drive = CreateOneDrive(organization.Id, connector.Id, connection.Id);
        var content = new Microsoft365IndexedContent
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            Microsoft365SourceId = drive.Id,
            ExternalContentId = "document-1",
            IsAvailable = true,
            CreatedAt = DateTimeOffset.Parse("2026-09-10T20:10:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-09-10T20:12:00Z")
        };
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.Add(organization);
        dbContext.OrganizationMembers.AddRange(
            CreateMember(organization.Id, "admin@metalpro.test", OrganizationRole.Admin, RecordStatus.Active),
            CreateMember(organization.Id, "disabled@metalpro.test", OrganizationRole.User, RecordStatus.Inactive));
        dbContext.OrganizationConnectors.Add(connector);
        dbContext.Microsoft365Connections.Add(connection);
        dbContext.Microsoft365Sources.AddRange(site, sharePointDrive, drive);
        dbContext.Microsoft365IndexedContents.Add(content);
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeOrganizationQueries(dbContext, new StubEmailBlindIndexHasher());

        // When
        var result = await queries.GetOrganizationDetailsAsync(
            organization.Id,
            CancellationToken.None);

        // Then
        Assert.NotNull(result);
        Assert.Equal(organization.Id, result.Organization.Id);
        Assert.Equal("tenant-metal", result.Organization.TenantId);
        Assert.Equal(2, result.Users.Total);
        Assert.Equal(1, result.Users.Active);
        Assert.True(result.Microsoft.Connected);
        Assert.True(result.Microsoft.AdminConsentGranted);
        Assert.Equal(1, result.Sources.SharePointSiteCount);
        Assert.Equal(1, result.Sources.OneDriveCount);
        Assert.Equal(1, result.Indexing.DocumentCount);
        Assert.Equal("Healthy", result.Indexing.Status);
    }

    [Theory, AutoDomainData]
    public async Task Given_IndexedSharePointDriveOnDiscoveredSite_When_GetOrganizationDetailsAsync_Then_CountsTheSite(
        Guid databaseId)
    {
        // Given
        var organization = CreateOrganization("MetalPro");
        var connectorId = Guid.NewGuid();
        var connection = new Microsoft365Connection
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            OrganizationConnectorId = connectorId,
            TenantId = "tenant-metal",
            Status = Microsoft365ConnectionStatus.Active,
            ConsentValidatedAt = DateTimeOffset.Parse("2026-09-10T20:00:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-09-10T19:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-09-10T20:00:00Z")
        };
        var site = CreateSite(organization.Id, connectorId, connection.Id);
        site.IsIndexed = false;
        var drive = CreateSharePointDrive(organization.Id, connectorId, connection.Id);
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.Add(organization);
        dbContext.Microsoft365Connections.Add(connection);
        dbContext.Microsoft365Sources.AddRange(site, drive);
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeOrganizationQueries(dbContext, new StubEmailBlindIndexHasher());

        // When
        var result = await queries.GetOrganizationDetailsAsync(
            organization.Id,
            CancellationToken.None);

        // Then
        Assert.NotNull(result);
        Assert.Equal(1, result.Sources.SharePointSiteCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_SourceInError_When_GetOrganizationDetailsAsync_Then_ReturnsErrorIndexingStatus(
        Guid databaseId)
    {
        // Given
        var organization = CreateOrganization("MetalPro");
        var connectorId = Guid.NewGuid();
        var connection = new Microsoft365Connection
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            OrganizationConnectorId = connectorId,
            TenantId = "tenant-metal",
            Status = Microsoft365ConnectionStatus.Active,
            ConsentValidatedAt = DateTimeOffset.Parse("2026-09-10T20:00:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-09-10T19:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-09-10T20:00:00Z")
        };
        var site = CreateSite(organization.Id, connectorId, connection.Id);
        site.Status = Microsoft365SourceStatus.Error;
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.Add(organization);
        dbContext.Microsoft365Connections.Add(connection);
        dbContext.Microsoft365Sources.Add(site);
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeOrganizationQueries(dbContext, new StubEmailBlindIndexHasher());

        // When
        var result = await queries.GetOrganizationDetailsAsync(
            organization.Id,
            CancellationToken.None);

        // Then
        Assert.NotNull(result);
        Assert.Equal("Error", result.Indexing.Status);
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

    private static OrganizationMember CreateMember(
        Guid organizationId,
        string email,
        OrganizationRole role,
        RecordStatus status) => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = email,
            Email = email,
            EmailLookupHash = new StubEmailBlindIndexHasher().ComputeHash(email),
            IdentityProvider = IdentityProvider.MicrosoftEntraId,
            ExternalUserId = Guid.NewGuid().ToString("D"),
            Role = role,
            Status = status,
            Version = 1
        };

    private static Microsoft365Site CreateSite(
        Guid organizationId,
        Guid connectorId,
        Guid connectionId) => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            OrganizationConnectorId = connectorId,
            Microsoft365ConnectionId = connectionId,
            Kind = Microsoft365SourceKind.SharePointSite,
            ExternalResourceId = "site-1",
            DisplayName = "SharePoint",
            Status = Microsoft365SourceStatus.Enabled,
            IsIndexed = true,
            SiteId = "site-1",
            DiscoveredAt = DateTimeOffset.Parse("2026-09-10T20:00:00Z"),
            LastSuccessfulSynchronizationAt = DateTimeOffset.Parse("2026-09-10T20:12:00Z")
        };

    private static Microsoft365Drive CreateOneDrive(
        Guid organizationId,
        Guid connectorId,
        Guid connectionId) => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            OrganizationConnectorId = connectorId,
            Microsoft365ConnectionId = connectionId,
            Kind = Microsoft365SourceKind.OneDrive,
            ExternalResourceId = "drive-1",
            DisplayName = "OneDrive",
            Status = Microsoft365SourceStatus.Enabled,
            IsIndexed = true,
            SiteId = "site-1",
            DriveId = "drive-1",
            DiscoveredAt = DateTimeOffset.Parse("2026-09-10T20:00:00Z"),
            LastSuccessfulSynchronizationAt = DateTimeOffset.Parse("2026-09-10T20:12:00Z")
        };

    private static Microsoft365Drive CreateSharePointDrive(
        Guid organizationId,
        Guid connectorId,
        Guid connectionId) => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            OrganizationConnectorId = connectorId,
            Microsoft365ConnectionId = connectionId,
            Kind = Microsoft365SourceKind.SharePointDrive,
            ExternalResourceId = "sharepoint-drive-1",
            DisplayName = "Documents",
            Status = Microsoft365SourceStatus.Enabled,
            IsIndexed = true,
            SiteId = "site-1",
            DriveId = "sharepoint-drive-1",
            DiscoveredAt = DateTimeOffset.Parse("2026-09-10T20:00:00Z"),
            LastSuccessfulSynchronizationAt = DateTimeOffset.Parse("2026-09-10T20:12:00Z")
        };
}
