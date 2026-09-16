namespace AssistantCore.Service.Application.Models.Backoffice;

/// <summary>
/// Corps de la demande interne de reprise. L'organisation ciblee vient de l'URL, jamais du corps.
/// </summary>
public sealed record BackofficeMicrosoft365ReindexRequest(string? Reason);

public sealed record BackofficeMicrosoft365ReindexResponse(
    Guid OperationId,
    Guid OrganizationId,
    string Status,
    int LibraryCount,
    DateTimeOffset RequestedAt);

public sealed record BackofficeMicrosoft365ReindexStatusResponse(
    Guid OperationId,
    Guid OrganizationId,
    string Status,
    int CompletedLibraryCount,
    int LibraryCount,
    int DiscoveredDocumentCount,
    int ProcessedDocumentCount,
    int IgnoredDocumentCount,
    int FailedDocumentCount,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? LastErrorCode,
    string? Reason);
