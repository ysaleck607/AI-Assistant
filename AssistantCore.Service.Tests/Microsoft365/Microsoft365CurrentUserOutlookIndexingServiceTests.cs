using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365CurrentUserOutlookIndexingServiceTests
{
    [Theory, AutoDomainData]
    public async Task Given_AnActiveMicrosoft365Connection_When_EnsureIndexedAsync_Then_CurrentUserMailFoldersAreAutomaticallyActivated(
        Guid organizationId,
        Guid entraUserId)
    {
        // Given
        await using var dbContext = CreateDbContext();
        await SeedActiveConnectionAsync(dbContext, organizationId, "tenant-1");
        var folders = new[]
        {
            new Microsoft365CurrentUserOutlookFolder("inbox", "Inbox", "Inbox"),
            new Microsoft365CurrentUserOutlookFolder("sentitems", "Sent Items", "Sent Items"),
            new Microsoft365CurrentUserOutlookFolder("archive-child", "Project", "Archive/Project")
        };
        var foldersClient = new StubOutlookFoldersClient(folders);
        var service = CreateService(dbContext, foldersClient);

        // When
        await service.EnsureIndexedAsync(
            organizationId,
            entraUserId.ToString("D"),
            CancellationToken.None);

        // Then
        var mailboxes = await dbContext.Microsoft365Sources
            .Include(candidate => candidate.Synchronizations)
            .Include(candidate => candidate.Subscriptions)
            .OrderBy(candidate => candidate.ParentExternalResourceId)
            .ToArrayAsync();
        Assert.Equal(3, mailboxes.Length);
        Assert.All(mailboxes, mailbox =>
        {
            Assert.Equal(Microsoft365SourceKind.OutlookMailbox, mailbox.Kind);
            Assert.Equal(entraUserId.ToString("D"), mailbox.ExternalResourceId);
            Assert.True(mailbox.IsIndexed);
            Assert.Equal(Microsoft365SourceStatus.Enabled, mailbox.Status);
        });
        Assert.Contains(mailboxes, mailbox =>
            mailbox.ParentExternalResourceId == "inbox"
            && mailbox.DisplayName == "Outlook - Inbox");
        Assert.Contains(mailboxes, mailbox =>
            mailbox.ParentExternalResourceId == "sentitems"
            && mailbox.DisplayName == "Outlook - Sent Items");
        Assert.Contains(mailboxes, mailbox =>
            mailbox.ParentExternalResourceId == "archive-child"
            && mailbox.DisplayName == "Outlook - Archive/Project");
        Assert.All(mailboxes, mailbox => Assert.Contains(mailbox.Synchronizations, synchronization =>
            synchronization.Type == Microsoft365SynchronizationType.Initial
            && synchronization.Status == Microsoft365SynchronizationStatus.Pending));
        Assert.All(mailboxes, mailbox => Assert.Contains(mailbox.Subscriptions, subscription =>
            subscription.Status == Microsoft365SubscriptionStatus.Pending
            && subscription.Resource == $"/users/{entraUserId:D}/mailFolders/{mailbox.ParentExternalResourceId}/messages"));

        var source = Assert.Single(await dbContext.OrganizationConnectorSources.ToArrayAsync());
        Assert.Equal(Microsoft365SourceType.Outlook, source.SourceType);
        Assert.Equal(RecordStatus.Active, source.Status);
        Assert.True(source.IsIndexed);
        Assert.Equal("tenant-1", foldersClient.ReceivedTenantId);
        Assert.Equal(entraUserId.ToString("D"), foldersClient.ReceivedEntraUserId);
    }

    [Theory, AutoDomainData]
    public async Task Given_NoActiveMicrosoft365Connection_When_EnsureIndexedAsync_Then_NothingIsPersisted(
        Guid organizationId,
        Guid entraUserId)
    {
        // Given
        await using var dbContext = CreateDbContext();
        var foldersClient = new StubOutlookFoldersClient([]);
        var service = CreateService(dbContext, foldersClient);

        // When
        await service.EnsureIndexedAsync(
            organizationId,
            entraUserId.ToString("D"),
            CancellationToken.None);

        // Then
        Assert.Empty(await dbContext.Microsoft365Sources.ToArrayAsync());
        Assert.Empty(await dbContext.OrganizationConnectorSources.ToArrayAsync());
        Assert.Null(foldersClient.ReceivedTenantId);
    }

    private static Microsoft365CurrentUserOutlookIndexingService CreateService(
        AssistantCoreDbContext dbContext,
        IMicrosoft365CurrentUserOutlookFoldersClient foldersClient) =>
        new(
            new Microsoft365ConnectionRepository(dbContext),
            new Microsoft365SourceDiscoveryRepository(dbContext),
            foldersClient,
            TimeProvider.System);

    private static async Task SeedActiveConnectionAsync(
        AssistantCoreDbContext dbContext,
        Guid organizationId,
        string tenantId)
    {
        var connectorId = Guid.NewGuid();
        dbContext.OrganizationConnectors.Add(new OrganizationConnector
        {
            Id = connectorId,
            OrganizationId = organizationId,
            Type = ConnectorType.Microsoft365,
            Status = RecordStatus.Active,
            IsConfigured = true
        });
        dbContext.Microsoft365Connections.Add(new Microsoft365Connection
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            OrganizationConnectorId = connectorId,
            TenantId = tenantId,
            Status = Microsoft365ConnectionStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    private static AssistantCoreDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AssistantCoreDbContext(options);
    }

    private sealed class StubOutlookFoldersClient(
        IReadOnlyCollection<Microsoft365CurrentUserOutlookFolder> folders)
        : IMicrosoft365CurrentUserOutlookFoldersClient
    {
        public string? ReceivedTenantId { get; private set; }
        public string? ReceivedEntraUserId { get; private set; }

        public Task<IReadOnlyCollection<Microsoft365CurrentUserOutlookFolder>> GetIndexableFoldersAsync(
            string tenantId,
            string entraUserId,
            CancellationToken cancellationToken = default)
        {
            ReceivedTenantId = tenantId;
            ReceivedEntraUserId = entraUserId;
            return Task.FromResult(folders);
        }
    }
}
