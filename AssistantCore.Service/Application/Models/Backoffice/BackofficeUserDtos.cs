namespace AssistantCore.Service.Application.Models.Backoffice;

public sealed record BackofficeUserListResponse(
    IReadOnlyCollection<BackofficeUserSummaryDto> Items);

public sealed record BackofficeUserSummaryDto(
    Guid Id,
    string Name,
    string Email,
    string Role,
    string AccessStatus,
    string OnboardingStatus,
    DateTimeOffset? LastSuccessfulAuthenticationAt,
    BackofficeAccessDiagnosticDto Diagnostic);

public sealed record BackofficeUserDetailsDto(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string Email,
    string IdentityProvider,
    string ExternalUserId,
    string Role,
    string AccessStatus,
    string OnboardingStatus,
    DateTimeOffset? LastSuccessfulAuthenticationAt,
    BackofficeAccessDiagnosticDto Diagnostic);

public sealed record BackofficeAccessDiagnosticDto(
    bool AccessAllowed,
    string Code,
    string Message,
    IReadOnlyCollection<string> Reasons);

public sealed record BackofficeAccessReevaluationResultDto(
    Guid UserId,
    BackofficeAccessDiagnosticDto Diagnostic,
    string CorrelationId,
    DateTimeOffset EvaluatedAt);
