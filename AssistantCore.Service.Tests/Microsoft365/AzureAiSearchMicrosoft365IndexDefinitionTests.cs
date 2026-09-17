using AssistantCore.ExternalServices.Services.Azure;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class AzureAiSearchMicrosoft365IndexDefinitionTests
{
    [Theory, AutoDomainData]
    public void Given_TheExistingAzureIndex_When_CreateFields_Then_ArchivePathMatchesTheExistingSchema(Guid _)
    {
        // Given
        const string archivePathFieldName = "archivePath";

        // When
        var field = AzureAiSearchMicrosoft365IndexDefinition.CreateFields()
            .Single(field => field.Name == archivePathFieldName);

        // Then
        Assert.Equal("Edm.String", field.Type);
        Assert.True(field.Filterable);
        Assert.True(field.Retrievable);
        Assert.False(field.Searchable);
    }
}
