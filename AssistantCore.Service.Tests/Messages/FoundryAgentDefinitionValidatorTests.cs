using System.Text.Json;
using AssistantCore.ExternalServices.Services.Foundry;

namespace AssistantCore.Service.Tests.Messages;

public sealed class FoundryAgentDefinitionValidatorTests
{
    [Theory, AutoDomainData]
    public void Given_RequiredFunctionsWithoutWebSearch_When_Validate_Then_AcceptsDefinition()
    {
        // Given
        var definition = JsonSerializer.SerializeToElement(new
        {
            tools = new[]
            {
                new { type = "function", name = "EnterpriseSearch" },
                new { type = "function", name = "AnalyzeSpreadsheet" },
                new { type = "function", name = "QueryOutlookMailbox" }
            }
        });

        // When
        var exception = Record.Exception(() =>
            FoundryAgentDefinitionValidator.Validate(definition));

        // Then
        Assert.Null(exception);
    }

    [Theory, AutoDomainData]
    public void Given_NestedRequiredFunctionDefinitions_When_Validate_Then_AcceptsDefinition()
    {
        // Given
        var definition = JsonSerializer.SerializeToElement(new
        {
            tools = new object[]
            {
                new
                {
                    type = "function",
                    function = new { name = "EnterpriseSearch" }
                },
                new
                {
                    type = "function",
                    function = new { name = "AnalyzeSpreadsheet" }
                },
                new
                {
                    type = "function",
                    function = new { name = "QueryOutlookMailbox" }
                }
            }
        });

        // When
        var exception = Record.Exception(() =>
            FoundryAgentDefinitionValidator.Validate(definition));

        // Then
        Assert.Null(exception);
    }

    [Theory, AutoDomainData]
    public void Given_MissingEnterpriseSearch_When_Validate_Then_RejectsDefinition()
    {
        // Given
        var definition = JsonSerializer.SerializeToElement(new { tools = Array.Empty<object>() });

        // When
        var exception = Assert.Throws<InvalidOperationException>(() =>
            FoundryAgentDefinitionValidator.Validate(definition));

        // Then
        Assert.Contains("EnterpriseSearch", exception.Message, StringComparison.Ordinal);
    }

    [Theory, AutoDomainData]
    public void Given_MissingQueryOutlookMailbox_When_Validate_Then_RejectsDefinition()
    {
        // Given
        var definition = JsonSerializer.SerializeToElement(new
        {
            tools = new[]
            {
                new { type = "function", name = "EnterpriseSearch" },
                new { type = "function", name = "AnalyzeSpreadsheet" }
            }
        });

        // When
        var exception = Assert.Throws<InvalidOperationException>(() =>
            FoundryAgentDefinitionValidator.Validate(definition));

        // Then
        Assert.Contains("QueryOutlookMailbox", exception.Message, StringComparison.Ordinal);
    }

    [Theory, AutoDomainData]
    public void Given_WebSearchAlongsideEnterpriseSearch_When_Validate_Then_RejectsDefinition()
    {
        // Given
        var definition = JsonSerializer.SerializeToElement(new
        {
            tools = new[]
            {
                new { type = "function", name = "EnterpriseSearch" },
                new { type = "function", name = "AnalyzeSpreadsheet" },
                new { type = "function", name = "QueryOutlookMailbox" },
                new { type = "web_search", name = string.Empty }
            }
        });

        // When
        var exception = Assert.Throws<InvalidOperationException>(() =>
            FoundryAgentDefinitionValidator.Validate(definition));

        // Then
        Assert.Contains("web_search", exception.Message, StringComparison.Ordinal);
    }
}
