using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Service.Tests.Repository;

public sealed class Microsoft365PersistenceConfigurationTests
{
    [Theory, AutoDomainData]
    public void Given_Microsoft365Models_When_InspectingConfiguration_Then_UniqueTenantAndSourceConstraintsAreConfigured(
        Guid databaseId)
    {
        // Given
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(databaseId.ToString())
            .Options;
        using var dbContext = new AssistantCoreDbContext(options);

        // When
        var connectionType = dbContext.Model.FindEntityType(typeof(Microsoft365Connection));
        var sourceType = dbContext.Model.FindEntityType(typeof(Microsoft365Source));
        var siteType = dbContext.Model.FindEntityType(typeof(Microsoft365Site));
        var driveType = dbContext.Model.FindEntityType(typeof(Microsoft365Drive));
        var listType = dbContext.Model.FindEntityType(typeof(Microsoft365List));
        var subscriptionType = dbContext.Model.FindEntityType(typeof(Microsoft365Subscription));
        var synchronizationType = dbContext.Model.FindEntityType(typeof(Microsoft365Synchronization));
        var listItemWorkType = dbContext.Model.FindEntityType(typeof(Microsoft365ListItemWork));
        var documentWorkType = dbContext.Model.FindEntityType(typeof(Microsoft365DocumentWork));
        var indexedContentType = dbContext.Model.FindEntityType(typeof(Microsoft365IndexedContent));
        var indexedPassageType = dbContext.Model.FindEntityType(typeof(Microsoft365IndexedPassage));

        // Then
        Assert.NotNull(connectionType);
        Assert.Contains(connectionType.GetIndexes(), index =>
            index.IsUnique && HasProperties(index, nameof(Microsoft365Connection.OrganizationId)));
        Assert.Contains(connectionType.GetIndexes(), index =>
            index.IsUnique && HasProperties(index, nameof(Microsoft365Connection.TenantId)));
        Assert.Contains(connectionType.GetIndexes(), index =>
            index.IsUnique && HasProperties(index, nameof(Microsoft365Connection.ConsentStateHash)));

        Assert.NotNull(sourceType);
        Assert.Contains(sourceType.GetIndexes(), index =>
            index.IsUnique && HasProperties(
                index,
                nameof(Microsoft365Source.Microsoft365ConnectionId),
                nameof(Microsoft365Source.Kind),
                nameof(Microsoft365Source.ExternalResourceId)));

        Assert.NotNull(siteType);
        Assert.Equal("Microsoft365Site", siteType.GetTableName());
        Assert.Contains(siteType.GetIndexes(), index =>
            index.IsUnique && HasProperties(
                index,
                nameof(Microsoft365Site.OrganizationId),
                nameof(Microsoft365Site.OrganizationConnectorId),
                nameof(Microsoft365Site.SiteId)));

        Assert.NotNull(driveType);
        Assert.Equal("Microsoft365Drive", driveType.GetTableName());
        Assert.True(driveType.FindProperty(nameof(Microsoft365Drive.SiteId))!.IsNullable);
        Assert.Equal(400, driveType.FindProperty(nameof(Microsoft365Drive.OwnerUserObjectId))?.GetMaxLength());
        Assert.Equal(320, driveType.FindProperty(nameof(Microsoft365Drive.OwnerUserPrincipalName))?.GetMaxLength());
        Assert.Contains(driveType.GetIndexes(), index =>
            index.IsUnique && HasProperties(
                index,
                nameof(Microsoft365Drive.OrganizationId),
                nameof(Microsoft365Drive.DriveId)));
        Assert.Contains(driveType.GetIndexes(), index =>
            HasProperties(
                index,
                nameof(Microsoft365Drive.OrganizationId),
                nameof(Microsoft365Drive.OwnerUserObjectId)));

        Assert.NotNull(listType);
        Assert.Equal("Microsoft365List", listType.GetTableName());
        Assert.NotNull(listType.FindProperty(nameof(Microsoft365List.DisplayName)));
        Assert.NotNull(listType.FindProperty(nameof(Microsoft365List.WebUrl)));
        Assert.NotNull(listType.FindProperty(nameof(Microsoft365List.Status)));
        Assert.NotNull(listType.FindProperty(nameof(Microsoft365List.IsIndexed)));
        Assert.Equal(
            64,
            listType.FindProperty(nameof(Microsoft365List.SchemaFingerprint))?.GetMaxLength());
        Assert.NotNull(listType.FindProperty(nameof(Microsoft365List.RequiresItemReprocessing)));
        Assert.Contains(listType.GetIndexes(), index =>
            index.IsUnique && HasProperties(
                index,
                nameof(Microsoft365List.OrganizationId),
                nameof(Microsoft365List.OrganizationConnectorId),
                nameof(Microsoft365List.SiteId),
                nameof(Microsoft365List.ListId)));

        Assert.NotNull(subscriptionType);
        Assert.NotNull(subscriptionType.FindProperty(nameof(Microsoft365Subscription.OrganizationId)));
        Assert.NotNull(subscriptionType.FindProperty(nameof(Microsoft365Subscription.Resource)));
        Assert.Contains(subscriptionType.GetIndexes(), index =>
            index.IsUnique && HasProperties(index, nameof(Microsoft365Subscription.MicrosoftSubscriptionId)));
        Assert.Contains(subscriptionType.GetIndexes(), index =>
            index.IsUnique
            && HasProperties(index, nameof(Microsoft365Subscription.Microsoft365SourceId))
            && string.Equals(
                index.GetFilter(),
                "[Status] = 'Active'",
                StringComparison.Ordinal));

        Assert.NotNull(synchronizationType);
        Assert.Contains(synchronizationType.GetIndexes(), index =>
            index.IsUnique && HasProperties(index, nameof(Microsoft365Synchronization.Microsoft365SourceId)));

        Assert.NotNull(listItemWorkType);
        Assert.Contains(listItemWorkType.GetIndexes(), index =>
            index.IsUnique
            && HasProperties(index, nameof(Microsoft365ListItemWork.DeduplicationKey)));

        Assert.NotNull(documentWorkType);
        Assert.Contains(documentWorkType.GetIndexes(), index =>
            index.IsUnique
            && HasProperties(index, nameof(Microsoft365DocumentWork.DeduplicationKey)));

        Assert.NotNull(indexedContentType);
        Assert.Equal(64, indexedContentType.FindProperty(nameof(Microsoft365IndexedContent.AclFingerprint))?.GetMaxLength());
        Assert.IsType<NullableEncryptedStringConverter>(
            indexedContentType.FindProperty(nameof(Microsoft365IndexedContent.SiteUrl))?.GetValueConverter());
        Assert.IsType<NullableEncryptedStringConverter>(
            indexedContentType.FindProperty(nameof(Microsoft365IndexedContent.Title))?.GetValueConverter());
        Assert.IsType<NullableEncryptedStringConverter>(
            indexedContentType.FindProperty(nameof(Microsoft365IndexedContent.WebUrl))?.GetValueConverter());

        Assert.IsType<EncryptedStringConverter>(
            sourceType.FindProperty(nameof(Microsoft365Source.DisplayName))?.GetValueConverter());
        Assert.IsType<NullableEncryptedStringConverter>(
            sourceType.FindProperty(nameof(Microsoft365Source.WebUrl))?.GetValueConverter());
        Assert.IsType<NullableEncryptedStringConverter>(
            sourceType.FindProperty(nameof(Microsoft365Source.DeltaLink))?.GetValueConverter());

        Assert.IsType<NullableEncryptedStringConverter>(
            listItemWorkType.FindProperty(nameof(Microsoft365ListItemWork.WebUrl))?.GetValueConverter());
        Assert.IsType<NullableEncryptedStringConverter>(
            listItemWorkType.FindProperty(nameof(Microsoft365ListItemWork.FieldsJson))?.GetValueConverter());

        Assert.IsType<NullableEncryptedStringConverter>(
            documentWorkType.FindProperty(nameof(Microsoft365DocumentWork.Name))?.GetValueConverter());
        Assert.IsType<NullableEncryptedStringConverter>(
            documentWorkType.FindProperty(nameof(Microsoft365DocumentWork.WebUrl))?.GetValueConverter());
        Assert.Contains(indexedContentType.GetIndexes(), index =>
            HasProperties(index, nameof(Microsoft365IndexedContent.NextAclReconciliationAt)));
        Assert.Contains(indexedContentType.GetIndexes(), index =>
            index.IsUnique
            && HasProperties(
                index,
                nameof(Microsoft365IndexedContent.OrganizationId),
                nameof(Microsoft365IndexedContent.Microsoft365SourceId),
                nameof(Microsoft365IndexedContent.ExternalContentId)));

        Assert.NotNull(indexedPassageType);
        Assert.Contains(indexedPassageType.GetIndexes(), index =>
            index.IsUnique
            && HasProperties(index, nameof(Microsoft365IndexedPassage.ChunkId)));
    }

    private static bool HasProperties(
        Microsoft.EntityFrameworkCore.Metadata.IReadOnlyIndex index,
        params string[] propertyNames) =>
        index.Properties.Select(property => property.Name).SequenceEqual(propertyNames);
}
