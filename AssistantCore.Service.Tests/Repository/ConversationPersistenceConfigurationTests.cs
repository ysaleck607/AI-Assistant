using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AssistantCore.Service.Tests.Repository;

public sealed class ConversationPersistenceConfigurationTests
{
    [Fact]
    public void Given_ConversationModel_When_InspectingConfiguration_Then_RelationsConstraintsAndLookupIndexAreConfigured()
    {
        // Given
        using var dbContext = CreateDbContext();

        // When
        var entityType = dbContext.Model.FindEntityType(typeof(Conversation));

        // Then
        Assert.NotNull(entityType);
        Assert.IsType<EncryptedStringConverter>(
            entityType.FindProperty(nameof(Conversation.Title))?.GetValueConverter());
        Assert.Equal(20, entityType.FindProperty(nameof(Conversation.Status))?.GetMaxLength());
        AssertIndex(
            entityType,
            nameof(Conversation.OrganizationId),
            nameof(Conversation.OwnerMemberId),
            nameof(Conversation.Id));
        AssertForeignKey(
            entityType,
            typeof(Organization),
            DeleteBehavior.Restrict,
            nameof(Conversation.OrganizationId));
        AssertForeignKey(
            entityType,
            typeof(OrganizationMember),
            DeleteBehavior.Restrict,
            nameof(Conversation.OwnerMemberId));
    }

    private static AssistantCoreDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AssistantCoreDbContext(options);
    }

    private static void AssertIndex(
        IReadOnlyEntityType entityType,
        params string[] propertyNames)
    {
        Assert.Contains(
            entityType.GetIndexes(),
            index => index.Properties.Select(property => property.Name)
                .SequenceEqual(propertyNames));
    }

    private static void AssertForeignKey(
        IReadOnlyEntityType entityType,
        Type principalType,
        DeleteBehavior deleteBehavior,
        params string[] propertyNames)
    {
        Assert.Contains(
            entityType.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == principalType
                && foreignKey.DeleteBehavior == deleteBehavior
                && foreignKey.Properties.Select(property => property.Name)
                    .SequenceEqual(propertyNames));
    }
}
