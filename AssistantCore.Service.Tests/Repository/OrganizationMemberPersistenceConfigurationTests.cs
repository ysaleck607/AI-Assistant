using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Service.Tests.Repository;

public sealed class OrganizationMemberPersistenceConfigurationTests
{
    [Theory, AutoDomainData]
    public void Given_OrganizationMemberModel_When_InspectingConfiguration_Then_OnlyTheIdentityIndexIsUnique(
        Guid databaseId)
    {
        // Given
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(databaseId.ToString())
            .Options;
        using var dbContext = new AssistantCoreDbContext(options);

        // When
        var memberType = dbContext.Model.FindEntityType(typeof(OrganizationMember));

        // Then
        Assert.NotNull(memberType);

        // A guest's client-tenant row can legitimately share an email with an unrelated
        // internal member; email must never block or merge distinct identities.
        Assert.DoesNotContain(
            memberType.GetIndexes(),
            index => HasProperties(index, nameof(OrganizationMember.OrganizationId), nameof(OrganizationMember.Email)));

        Assert.Contains(
            memberType.GetIndexes(),
            index => index.IsUnique
                && HasProperties(
                    index,
                    nameof(OrganizationMember.OrganizationId),
                    nameof(OrganizationMember.IdentityProvider),
                    nameof(OrganizationMember.ExternalUserId)));
    }

    [Theory, AutoDomainData]
    public void Given_OrganizationMemberModel_When_InspectingConfiguration_Then_NameAndEmailAreEncryptedAndTheBlindIndexIsUnique(
        Guid databaseId)
    {
        // Given
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(databaseId.ToString())
            .Options;
        using var dbContext = new AssistantCoreDbContext(options);

        // When
        var memberType = dbContext.Model.FindEntityType(typeof(OrganizationMember));

        // Then
        Assert.NotNull(memberType);
        Assert.IsType<EncryptedStringConverter>(
            memberType.FindProperty(nameof(OrganizationMember.Name))?.GetValueConverter());
        Assert.IsType<EncryptedStringConverter>(
            memberType.FindProperty(nameof(OrganizationMember.Email))?.GetValueConverter());

        // Team decision (2026-09-18): keep email unique per organization to avoid
        // permission complexity ahead of the client deployment. A guest sharing an
        // email with an internal member (see #31/#73) is deferred, not supported yet.
        Assert.Contains(
            memberType.GetIndexes(),
            index => index.IsUnique
                && HasProperties(
                    index,
                    nameof(OrganizationMember.OrganizationId),
                    nameof(OrganizationMember.EmailLookupHash)));
    }

    private static bool HasProperties(
        Microsoft.EntityFrameworkCore.Metadata.IReadOnlyIndex index,
        params string[] propertyNames) =>
        index.Properties.Select(property => property.Name).SequenceEqual(propertyNames);
}
