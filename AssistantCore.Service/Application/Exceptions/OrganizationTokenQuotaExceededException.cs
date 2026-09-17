namespace AssistantCore.Service.Application.Exceptions;

public sealed class OrganizationTokenQuotaExceededException(DateTimeOffset periodEndsAt)
    : Exception("Le quota de jetons de l'organisation est épuisé."), IErrorCodeException
{
    public const string Code = "organization_token_quota_exhausted";

    public string ErrorCode => Code;

    public DateTimeOffset PeriodEndsAt { get; } = periodEndsAt;
}
