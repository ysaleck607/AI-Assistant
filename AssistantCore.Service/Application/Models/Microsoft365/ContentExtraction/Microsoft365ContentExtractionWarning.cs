namespace AssistantCore.Service.Application.Models.Microsoft365.ContentExtraction;

public enum Microsoft365ContentExtractionWarning
{
    MacroIgnored,
    ExternalLinkIgnored,
    EmbeddedObjectIgnored,
    HiddenSheetIgnored,
    FormulaValueUnavailable,
    PartialArchive,
    UnsafeArchivePath,
    DuplicateArchiveEntry,
    UnsupportedArchiveEntry,
    CorruptedArchiveEntry,
    ArchiveDepthExceeded,
    ArchiveTooLarge,
    EncryptedArchive,
    CorruptedArchive
}
