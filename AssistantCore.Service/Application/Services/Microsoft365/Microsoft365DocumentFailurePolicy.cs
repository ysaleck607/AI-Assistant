using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public static class Microsoft365DocumentFailurePolicy
{
    public static Microsoft365DocumentFailureDecision Evaluate(
        Exception exception,
        int attemptCount,
        Microsoft365Options options)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(options);

        if (attemptCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptCount));
        }

        if (exception is Microsoft365AclResolutionException)
        {
            var retryMinutes = attemptCount >= options.DocumentWorkMaximumAttempts
                ? options.AclReconciliationIntervalMinutes
                : options.DocumentWorkRetryMinutes;
            return new Microsoft365DocumentFailureDecision(
                IsPermanent: false,
                TimeSpan.FromMinutes(retryMinutes));
        }

        if (exception is Microsoft365ExternalException { IsTransient: true } externalException)
        {
            return new Microsoft365DocumentFailureDecision(
                IsPermanent: false,
                externalException.RetryAfterDelay
                    ?? TimeSpan.FromMinutes(Math.Min(60, Math.Max(1, Math.Pow(2, attemptCount)) * options.DocumentWorkRetryMinutes)));
        }

        var isPermanent = attemptCount >= options.DocumentWorkMaximumAttempts
            || exception is InvalidDataException or ArgumentException
            || exception is Microsoft365ContentExtractionException { IsTransient: false };
        return new Microsoft365DocumentFailureDecision(
            isPermanent,
            TimeSpan.FromMinutes(options.DocumentWorkRetryMinutes));
    }
}

public sealed record Microsoft365DocumentFailureDecision(
    bool IsPermanent,
    TimeSpan RetryDelay);
