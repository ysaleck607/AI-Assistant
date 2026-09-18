namespace AssistantCore.Service.Application.Exceptions;

public sealed class RequestRateLimitExceededException(int retryAfterSeconds)
    : Exception("Trop de demandes ont été envoyées."), IErrorCodeException
{
    public const string Code = "request_rate_limit_exceeded";

    public string ErrorCode => Code;

    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}
