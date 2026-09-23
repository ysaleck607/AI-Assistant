namespace AssistantCore.ExternalServices.Services.OpenAI;

public sealed class OpenAiExternalException(
    int statusCode,
    string? providerErrorMessage = null,
    TimeSpan? retryAfterDelay = null,
    DateTimeOffset? retryAfterAt = null)
    : Exception($"The OpenAI request failed with status {statusCode}: {providerErrorMessage ?? "No provider details returned."}")
{
    public int StatusCode { get; } = statusCode;

    public string? ProviderErrorMessage { get; } = providerErrorMessage;

    public TimeSpan? RetryAfterDelay { get; } = retryAfterDelay;

    public DateTimeOffset? RetryAfterAt { get; } = retryAfterAt;

    public bool IsTransient => statusCode == 429 || statusCode >= 500;
}
