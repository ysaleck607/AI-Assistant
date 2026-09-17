namespace AssistantCore.Service.Application.Models.Microsoft365.ContentExtraction;

public sealed record Microsoft365ExtractedContentUnit(
    Microsoft365ExtractedContentUnitKind Kind,
    int Order,
    string Text,
    string SourcePart,
    string? ArchivePath = null);

public enum Microsoft365ExtractedContentUnitKind
{
    Page,
    Header,
    Title,
    Paragraph,
    ListItem,
    Table,
    Footer,
    Note
}
