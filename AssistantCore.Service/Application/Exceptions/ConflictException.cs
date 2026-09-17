namespace AssistantCore.Service.Application.Exceptions;

public sealed class ConflictException(string message, string? errorCode = null)
    : Exception(message), IErrorCodeException
{
    public const string ConversationArchived = "conversation_archived";

    public const string ConversationVersionConflict = "conversation_version_conflict";

    public const string MemberVersionConflict = "member_version_conflict";

    public const string Microsoft365ReindexAlreadyRunning = "microsoft365_reindex_already_running";

    public string ErrorCode { get; } = errorCode ?? "conflict";
}
