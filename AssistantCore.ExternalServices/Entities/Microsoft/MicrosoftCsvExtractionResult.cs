namespace AssistantCore.ExternalServices.Entities.Microsoft;

public sealed record MicrosoftCsvExtractionResult(
    MicrosoftCsvExtractionStatus Status,
    IReadOnlyList<MicrosoftCsvExtractedUnit> Units);

public sealed record MicrosoftCsvExtractedUnit(
    int Order,
    string Text,
    string SourcePart);

public enum MicrosoftCsvExtractionStatus
{
    Success,
    EmptyDocument,
    CorruptedDocument,
    TooLarge
}
