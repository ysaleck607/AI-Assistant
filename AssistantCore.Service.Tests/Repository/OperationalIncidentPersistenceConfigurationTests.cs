using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AssistantCore.Service.Tests.Repository;

public sealed class OperationalIncidentPersistenceConfigurationTests
{
    [Theory, AutoDomainData]
    public void Given_OperationalIncidentModel_When_InspectingConfiguration_Then_OrganizationIdIsOptional(
        Guid databaseId)
    {
        // Given
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(databaseId.ToString())
            .Options;
        using var dbContext = new AssistantCoreDbContext(options);

        // When
        var incidentType = dbContext.Model.FindEntityType(typeof(OperationalIncident));

        // Then
        Assert.NotNull(incidentType);

        // Certains incidents (demarrage worker, panne Foundry globale) ne sont rattaches a
        // aucune organisation : la FK ne doit jamais etre requise.
        var organizationForeignKey = incidentType.GetForeignKeys()
            .Single(foreignKey => foreignKey.Properties
                .Any(property => property.Name == nameof(OperationalIncident.OrganizationId)));
        Assert.False(organizationForeignKey.IsRequired);
    }

    [Theory, AutoDomainData]
    public void Given_OperationalIncidentModel_When_InspectingConfiguration_Then_TheFourExpectedIndexesExist(
        Guid databaseId)
    {
        // Given
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(databaseId.ToString())
            .Options;
        using var dbContext = new AssistantCoreDbContext(options);

        // When
        var incidentType = dbContext.Model.FindEntityType(typeof(OperationalIncident));

        // Then
        Assert.NotNull(incidentType);

        Assert.Contains(
            incidentType.GetIndexes(),
            index => HasProperties(
                index,
                nameof(OperationalIncident.OrganizationId),
                nameof(OperationalIncident.OccurredAt)));

        Assert.Contains(
            incidentType.GetIndexes(),
            index => HasProperties(index, nameof(OperationalIncident.CorrelationId)));

        Assert.Contains(
            incidentType.GetIndexes(),
            index => HasProperties(
                index,
                nameof(OperationalIncident.Subsystem),
                nameof(OperationalIncident.Severity),
                nameof(OperationalIncident.OccurredAt)));

        Assert.Contains(
            incidentType.GetIndexes(),
            index => HasProperties(
                index,
                nameof(OperationalIncident.RelatedResourceType),
                nameof(OperationalIncident.RelatedResourceId)));
    }

    [Theory, AutoDomainData]
    public void Given_OperationalIncidentModel_When_InspectingConfiguration_Then_EnumsAreStoredAsBoundedStrings(
        Guid databaseId)
    {
        // Given
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(databaseId.ToString())
            .Options;
        using var dbContext = new AssistantCoreDbContext(options);

        // When
        var incidentType = dbContext.Model.FindEntityType(typeof(OperationalIncident));

        // Then
        Assert.NotNull(incidentType);

        // HasConversion<string>() on an enum does not surface via GetValueConverter() on the
        // finalized InMemory model (same reason ConversationPersistenceConfigurationTests
        // asserts Conversation.Status via GetMaxLength() rather than GetValueConverter()).
        Assert.Equal(50, incidentType.FindProperty(nameof(OperationalIncident.Subsystem))?.GetMaxLength());
        Assert.Equal(50, incidentType.FindProperty(nameof(OperationalIncident.Severity))?.GetMaxLength());
        Assert.Equal(50, incidentType.FindProperty(nameof(OperationalIncident.Status))?.GetMaxLength());
    }

    private static bool HasProperties(IReadOnlyIndex index, params string[] propertyNames) =>
        index.Properties.Select(property => property.Name).SequenceEqual(propertyNames);
}
