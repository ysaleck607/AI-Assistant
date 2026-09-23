using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using AssistantCore.Repository.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AssistantCore.Service.Tests.Repository;

public sealed class Microsoft365PendingSynchronizationRepositoryTests
{
    [Theory, AutoDomainData]
    public async Task Given_APendingOutlookSynchronization_When_ClaimNextAsync_Then_ReturnsTheOutlookWork(
        Guid databaseId,
        Guid organizationId,
        Guid connectorId,
        Guid connectionId,
        Guid sourceId,
        Guid synchronizationId,
        DateTimeOffset requestedAt,
        DateTimeOffset startedAt)
    {
        // Given
        await using var dbContext = CreateDbContext(databaseId);
        var connection = new Microsoft365Connection
        {
            Id = connectionId,
            OrganizationId = organizationId,
            OrganizationConnectorId = connectorId,
            Status = Microsoft365ConnectionStatus.Active,
            CreatedAt = requestedAt.AddDays(-1),
            UpdatedAt = requestedAt.AddDays(-1)
        };
        var source = new Microsoft365Source
        {
            Id = sourceId,
            Microsoft365ConnectionId = connectionId,
            Kind = Microsoft365SourceKind.OutlookMailbox,
            ExternalResourceId = "user@contoso.com",
            DisplayName = "User mailbox",
            Status = Microsoft365SourceStatus.Enabled,
            IsIndexed = true,
            DiscoveredAt = requestedAt.AddDays(-1)
        };
        var synchronization = new Microsoft365Synchronization
        {
            Id = synchronizationId,
            Microsoft365SourceId = sourceId,
            Type = Microsoft365SynchronizationType.Initial,
            Status = Microsoft365SynchronizationStatus.Pending,
            RequestedAt = requestedAt
        };
        dbContext.AddRange(connection, source, synchronization);
        await dbContext.SaveChangesAsync();
        var repository = new Microsoft365PendingSynchronizationRepository(dbContext);

        // When
        var work = await repository.ClaimNextAsync(startedAt);

        // Then
        Assert.NotNull(work);
        Assert.Equal(synchronizationId, work.SynchronizationId);
        Assert.Equal(sourceId, work.SourceId);
        Assert.Equal(organizationId, work.OrganizationId);
        Assert.Equal(Microsoft365SourceKind.OutlookMailbox, work.SourceKind);
        Assert.Equal(Microsoft365SynchronizationStatus.Running, synchronization.Status);
        Assert.Equal(startedAt, synchronization.StartedAt);
        Assert.Equal(1, synchronization.AttemptCount);
    }

    private static AssistantCoreDbContext CreateDbContext(Guid databaseId) =>
        new(new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(databaseId.ToString())
            .ConfigureWarnings(warnings =>
                warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
}
