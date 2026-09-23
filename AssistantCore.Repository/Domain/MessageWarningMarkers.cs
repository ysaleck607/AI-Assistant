namespace AssistantCore.Repository.Domain;

/// <summary>
/// MessageWarning.Content is free-text and encrypted at rest, so a distinguishable
/// warning category is marked with a stable prefix instead of a new schema column.
/// This avoids a migration while still letting backoffice queries filter and detect
/// this specific category after decrypting the content in memory. Deliberately kept
/// out of Domain.Entities: DataClassificationTests treats every public class in that
/// namespace as a persisted entity requiring documentation, which this is not.
/// </summary>
public static class MessageWarningMarkers
{
    public const string NoEvidenceFoundPrefix = "[NoEvidenceFound]";
}
