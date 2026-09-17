namespace AssistantCore.ExternalServices.Entities.Microsoft;

public sealed record MicrosoftArchiveExtractionResult(
    MicrosoftArchiveExtractionStatus Status,
    IReadOnlyList<MicrosoftArchiveEntry> Entries,
    IReadOnlyList<MicrosoftArchiveExtractionWarning> Warnings);

public sealed record MicrosoftArchiveEntry(
    string ArchivePath,
    string FileName,
    byte[] Content,
    string? MimeType = null);

public enum MicrosoftArchiveExtractionStatus
{
    Success,
    EmptyArchive,
    TooLarge,
    EncryptedArchive,
    CorruptedArchive,
    UnsafeArchivePath,
    ArchiveDepthExceeded
}

public enum MicrosoftArchiveExtractionWarning
{
    PartialArchive,
    UnsafeArchivePath,
    DuplicateArchiveEntry,
    CorruptedArchiveEntry
}
