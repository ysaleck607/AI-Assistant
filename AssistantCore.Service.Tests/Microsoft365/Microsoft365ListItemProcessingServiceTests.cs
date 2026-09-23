using AssistantCore.Service.Application.Services.Microsoft365;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365ListItemProcessingServiceTests
{
    [Theory, AutoDomainData]
    public void Given_ListItemFields_When_BuildingIndexableContent_Then_FlattensBusinessValues(
        string itemId)
    {
        // Given
        const string fieldsJson = """
        {
          "Title": "Facture Atlas",
          "Amount": 2622.50,
          "Customer": {
            "Name": "Groupe Horizon",
            "Region": "Québec"
          },
          "Tags": ["urgent", "finance"],
          "Optional": null
        }
        """;

        // When
        var (title, content) = Microsoft365ListItemProcessingService.BuildIndexableContent(
            "Comptabilité",
            itemId,
            fieldsJson);

        // Then
        Assert.Equal("Comptabilité — Facture Atlas", title);
        Assert.Contains("Title: Facture Atlas", content, StringComparison.Ordinal);
        Assert.Contains("Amount: 2622.50", content, StringComparison.Ordinal);
        Assert.Contains("Customer.Name: Groupe Horizon", content, StringComparison.Ordinal);
        Assert.Contains("Customer.Region: Québec", content, StringComparison.Ordinal);
        Assert.Contains("Tags: urgent, finance", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Optional", content, StringComparison.Ordinal);
    }

    [Theory, AutoDomainData]
    public void Given_ListItemWithoutTitle_When_BuildingIndexableContent_Then_UsesStableItemFallback(
        string itemId)
    {
        // Given
        const string fieldsJson = """
        {
          "Code": "ORANGE-7429",
          "Enabled": true
        }
        """;

        // When
        var (title, content) = Microsoft365ListItemProcessingService.BuildIndexableContent(
            "Projets",
            itemId,
            fieldsJson);

        // Then
        Assert.Equal($"Projets — élément {itemId}", title);
        Assert.Contains("Code: ORANGE-7429", content, StringComparison.Ordinal);
        Assert.Contains("Enabled: true", content, StringComparison.Ordinal);
    }

    [Theory, AutoDomainData]
    public void Given_NonObjectListFields_When_BuildingIndexableContent_Then_RejectsPayload(
        string itemId)
    {
        // When
        Action action = () =>
        {
            _ = Microsoft365ListItemProcessingService.BuildIndexableContent(
                "Projets",
                itemId,
                "[1,2,3]");
        };

        // Then
        Assert.Throws<InvalidDataException>(action);
    }
}
