namespace AssistantCore.ExternalServices.Entities.Azure;

public sealed record AzureAiSearchIndexFieldDefinition(
    string Name,
    string Type,
    bool Key = false,
    bool Searchable = false,
    bool Filterable = false,
    bool Retrievable = true,
    string? Analyzer = null);
