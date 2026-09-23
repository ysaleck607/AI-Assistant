namespace AssistantCore.Service.Application.Exceptions;

public sealed class Microsoft365ExternalException(
    string message,
    Exception? innerException = null,
    bool isTransient = false,
    TimeSpan? retryAfterDelay = null)
    : Exception(message, innerException)
{
    public bool IsTransient { get; } = isTransient;

    public TimeSpan? RetryAfterDelay { get; } = retryAfterDelay;
}
