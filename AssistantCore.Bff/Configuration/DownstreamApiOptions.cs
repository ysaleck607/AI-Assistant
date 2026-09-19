namespace AssistantCore.Bff.Configuration;

public sealed class DownstreamApiOptions
{
    public const string SectionName = "DownstreamApi";

    public string BaseUrl { get; init; } = string.Empty;
    public string[] Scopes { get; init; } = [];
}
