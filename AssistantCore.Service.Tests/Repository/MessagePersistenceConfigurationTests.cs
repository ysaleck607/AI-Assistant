using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AssistantCore.Service.Tests.Repository;

public sealed class MessagePersistenceConfigurationTests
{
    [Fact]
    public void Given_MessageModel_When_InspectingConfiguration_Then_ConstraintsOrderIndexAndCascadeAreConfigured()
    {
        // Given
        using var dbContext = CreateDbContext();

        // When
        var entityType = dbContext.Model.FindEntityType(typeof(Message));

        // Then
        Assert.NotNull(entityType);
        Assert.Equal(20, entityType.FindProperty(nameof(Message.Role))?.GetMaxLength());
        Assert.Equal(20, entityType.FindProperty(nameof(Message.ProcessingStatus))?.GetMaxLength());
        Assert.Equal(100, entityType.FindProperty(nameof(Message.Model))?.GetMaxLength());
        Assert.Equal(100, entityType.FindProperty(nameof(Message.ProcessingErrorCode))?.GetMaxLength());
        Assert.IsType<EncryptedStringConverter>(
            entityType.FindProperty(nameof(Message.Content))?.GetValueConverter());
        AssertIndex(
            entityType,
            nameof(Message.ConversationId),
            nameof(Message.CreatedAt),
            nameof(Message.Id));
        AssertForeignKey(
            entityType,
            typeof(Conversation),
            DeleteBehavior.Cascade,
            nameof(Message.ConversationId));
    }

    [Fact]
    public void Given_MessageSourceModel_When_InspectingConfiguration_Then_ConstraintsLookupIndexAndCascadeAreConfigured()
    {
        // Given
        using var dbContext = CreateDbContext();

        // When
        var entityType = dbContext.Model.FindEntityType(typeof(MessageSource));

        // Then
        Assert.NotNull(entityType);
        Assert.Equal(50, entityType.FindProperty(nameof(MessageSource.SourceType))?.GetMaxLength());
        Assert.Equal(500, entityType.FindProperty(nameof(MessageSource.Title))?.GetMaxLength());
        Assert.Equal(500, entityType.FindProperty(nameof(MessageSource.Reference))?.GetMaxLength());
        Assert.Equal(2048, entityType.FindProperty(nameof(MessageSource.Url))?.GetMaxLength());
        AssertIndex(entityType, nameof(MessageSource.MessageId));
        AssertForeignKey(
            entityType,
            typeof(Message),
            DeleteBehavior.Cascade,
            nameof(MessageSource.MessageId));
    }

    [Fact]
    public void Given_MessageWarningModel_When_InspectingConfiguration_Then_ConstraintsLookupIndexAndCascadeAreConfigured()
    {
        // Given
        using var dbContext = CreateDbContext();

        // When
        var entityType = dbContext.Model.FindEntityType(typeof(MessageWarning));

        // Then
        Assert.NotNull(entityType);
        Assert.IsType<EncryptedStringConverter>(
            entityType.FindProperty(nameof(MessageWarning.Content))?.GetValueConverter());
        AssertIndex(entityType, nameof(MessageWarning.MessageId));
        AssertForeignKey(
            entityType,
            typeof(Message),
            DeleteBehavior.Cascade,
            nameof(MessageWarning.MessageId));
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
