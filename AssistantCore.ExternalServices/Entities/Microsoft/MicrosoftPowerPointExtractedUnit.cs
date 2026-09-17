namespace AssistantCore.ExternalServices.Entities.Microsoft;

public sealed record MicrosoftPowerPointExtractedUnit(
    MicrosoftPowerPointExtractedUnitKind Kind,
    int SlideNumber,
    int Order,
    string Text);

public enum MicrosoftPowerPointExtractedUnitKind
{
    Title,
    Paragraph,
    ListItem,
    Table,
    Note
}
