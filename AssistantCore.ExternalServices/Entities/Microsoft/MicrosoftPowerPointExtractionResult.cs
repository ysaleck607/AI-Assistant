namespace AssistantCore.ExternalServices.Entities.Microsoft;

public sealed record MicrosoftPowerPointExtractionResult(
    MicrosoftPowerPointExtractionStatus Status,
    IReadOnlyList<MicrosoftPowerPointExtractedUnit> Units,
    IReadOnlyList<MicrosoftPowerPointExtractionWarning> Warnings);

public enum MicrosoftPowerPointExtractionStatus
{
    Success,
    EmptyDocument,
    EncryptedDocument,
    CorruptedDocument,
    UnsupportedFormat,
    TooLarge
}

public enum MicrosoftPowerPointExtractionWarning
{
    MacroIgnored,
    ExternalLinkIgnored,
    EmbeddedObjectIgnored
}
