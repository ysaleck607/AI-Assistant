using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AssistantCore.Service.Tests.Repository;

public sealed class Microsoft365ReindexPersistenceConfigurationTests
{
    [Fact]
    public void Given_TheReindexOperationModel_When_InspectingConfiguration_Then_OnlyOneOperationCanBeActivePerOrganization()
    {
        // Given
        using var dbContext = CreateDbContext();

        // When
        var entityType = dbContext.Model.FindEntityType(typeof(Microsoft365ReindexOperation));

        // Then
        Assert.NotNull(entityType);
        Assert.Equal("Microsoft365ReindexOperation", entityType.GetTableName());
        var activeOperationIndex = Assert.Single(
            entityType.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(Microsoft365ReindexOperation.OrganizationId)]));
        Assert.Equal(
            "[Status] IN ('Pending', 'Running')",
            activeOperationIndex.GetFilter());
    }

    [Fact]
    public void Given_TheReindexOperationModel_When_InspectingConfiguration_Then_ColumnsMatchTheMigration()
    {
        // Given
        using var dbContext = CreateDbContext();

        // When
        var entityType = dbContext.Model.FindEntityType(typeof(Microsoft365ReindexOperation));

        // Then
        Assert.NotNull(entityType);
        Assert.Equal(30, entityType.FindProperty(nameof(Microsoft365ReindexOperation.Status))?.GetMaxLength());
        Assert.Equal(500, entityType.FindProperty(nameof(Microsoft365ReindexOperation.Reason))?.GetMaxLength());
        Assert.Equal(
            100,
            entityType.FindProperty(nameof(Microsoft365ReindexOperation.LastErrorCode))?.GetMaxLength());
        AssertForeignKey(
            entityType,
            typeof(Organization),
            DeleteBehavior.Restrict,
            nameof(Microsoft365ReindexOperation.OrganizationId));
        AssertForeignKey(
            entityType,
            typeof(Microsoft365Connection),
            DeleteBehavior.Restrict,
            nameof(Microsoft365ReindexOperation.Microsoft365ConnectionId));
    }

    [Fact]
    public void Given_TheSynchronizationModel_When_InspectingConfiguration_Then_ItLinksBackToItsReindexOperation()
    {
        // Given
        using var dbContext = CreateDbContext();

        // When
        var entityType = dbContext.Model.FindEntityType(typeof(Microsoft365Synchronization));

        // Then
        Assert.NotNull(entityType);
        var property = entityType.FindProperty(
            nameof(Microsoft365Synchronization.Microsoft365ReindexOperationId));
        Assert.NotNull(property);
        Assert.True(property.IsNullable);
        AssertForeignKey(
            entityType,
            typeof(Microsoft365ReindexOperation),
            DeleteBehavior.Restrict,
            nameof(Microsoft365Synchronization.Microsoft365ReindexOperationId));
    }

    private static AssistantCoreDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AssistantCoreDbContext(options);
    }

    private static void AssertForeignKey(
        IReadOnlyEntityType entityType,
        Type principalType,
        DeleteBehavior deleteBehavior,
        params string[] propertyNames)
    {
        var foreignKey = Assert.Single(
            entityType.GetForeignKeys(),
            key => key.PrincipalEntityType.ClrType == principalType
                && key.Properties.Select(property => property.Name).SequenceEqual(propertyNames));
        Assert.Equal(deleteBehavior, foreignKey.DeleteBehavior);
    }
}
