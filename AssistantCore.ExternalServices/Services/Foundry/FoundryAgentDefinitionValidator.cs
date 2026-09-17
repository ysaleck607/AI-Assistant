using System.Text.Json;

namespace AssistantCore.ExternalServices.Services.Foundry;

public static class FoundryAgentDefinitionValidator
{
    private static readonly string[] RequiredFunctionToolNames =
    [
        "EnterpriseSearch",
        "AnalyzeSpreadsheet",
        "QueryOutlookMailbox"
    ];

    public static void Validate(JsonElement definition)
    {
        if (!definition.TryGetProperty("tools", out var tools)
            || tools.ValueKind != JsonValueKind.Array)
        {
            throw CreateInvalidConfigurationException();
        }

        var declaredFunctionToolNames = tools
            .EnumerateArray()
            .Select(GetFunctionToolName)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var hasAllRequiredFunctionTools = RequiredFunctionToolNames.All(
            declaredFunctionToolNames.Contains);
        var hasWebSearch = tools.EnumerateArray().Any(tool =>
            HasStringProperty(tool, "type", "web_search"));

        if (!hasAllRequiredFunctionTools || hasWebSearch)
        {
            throw CreateInvalidConfigurationException();
        }
    }

    private static string? GetFunctionToolName(JsonElement tool)
    {
        if (!HasStringProperty(tool, "type", "function"))
        {
            return null;
        }

        if (tool.TryGetProperty("name", out var name)
            && name.ValueKind == JsonValueKind.String)
        {
            return name.GetString();
        }

        return tool.TryGetProperty("function", out var function)
            && function.ValueKind == JsonValueKind.Object
            && function.TryGetProperty("name", out var nestedName)
            && nestedName.ValueKind == JsonValueKind.String
                ? nestedName.GetString()
                : null;
    }

    private static bool HasStringProperty(
        JsonElement element,
        string propertyName,
        string expectedValue) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
        && string.Equals(property.GetString(), expectedValue, StringComparison.Ordinal);

    private static InvalidOperationException CreateInvalidConfigurationException() =>
        new(
            "The configured Foundry prompt agent must declare the local function tools "
            + $"'{string.Join("', '", RequiredFunctionToolNames)}' and must not declare 'web_search'.");
}
