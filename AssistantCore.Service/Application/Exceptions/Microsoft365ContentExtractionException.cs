using AssistantCore.Service.Application.Models.Microsoft365.ContentExtraction;

namespace AssistantCore.Service.Application.Exceptions;

public sealed class Microsoft365ContentExtractionException : Exception
{
    public Microsoft365ContentExtractionException(
        Microsoft365ContentExtractionStatus status,
        string? fileName = null,
        string? mimeType = null)
        : base(CreateMessage(status, fileName, mimeType))
    {
        if (status == Microsoft365ContentExtractionStatus.Success)
        {
            throw new ArgumentException(
                "A successful extraction cannot create a failure exception.",
                nameof(status));
        }

        Status = status;
    }

    public Microsoft365ContentExtractionStatus Status { get; }

    public string ErrorCode => Status.ToString();

    public bool IsTransient => Status is
        Microsoft365ContentExtractionStatus.OcrUnavailable or
        Microsoft365ContentExtractionStatus.OcrTimeout;

    private static string CreateMessage(
        Microsoft365ContentExtractionStatus status,
        string? fileName,
        string? mimeType)
    {
        var document = string.IsNullOrWhiteSpace(fileName)
            ? "Document"
            : $"Document '{fileName}'";
        var format = string.IsNullOrWhiteSpace(mimeType)
            ? string.Empty
            : $" (MIME type: {mimeType})";
        return $"{document} extraction ended with {status}{format}.";
    }
}
