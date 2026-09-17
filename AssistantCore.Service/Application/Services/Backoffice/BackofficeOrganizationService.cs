using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Queries;
using AssistantCore.Repository.Repositories;
using AssistantCore.Repository.Repositories.Audit;
using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Backoffice;
using Microsoft.Extensions.Logging;

namespace AssistantCore.Service.Application.Services.Backoffice;

public sealed class BackofficeOrganizationService(
    IBackofficeOrganizationQueries organizationQueries,
    IOrganizationMemberQueries memberQueries,
    IAdministrativeAuditRepository administrativeAuditRepository,
    ICurrentIdentity currentIdentity,
    ICorrelationIdProvider correlationIdProvider,
    ILogger<BackofficeOrganizationService> logger)
    : IBackofficeOrganizationService
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 25;
    private const int MaximumPageSize = 100;

    public async Task<BackofficeOrganizationListResponse> SearchOrganizationsAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = page <= 0 ? DefaultPage : page;
        var normalizedPageSize = pageSize <= 0
            ? DefaultPageSize
            : Math.Min(pageSize, MaximumPageSize);

        var result = await organizationQueries.SearchOrganizationsAsync(
            normalizedPage,
            normalizedPageSize,
            search,
            cancellationToken);

        return BackofficeOrganizationListResponse.FromData(result);
    }

    public async Task<BackofficeOrganizationDetailsDto> GetOrganizationDetailsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var data = await GetRequiredOrganizationAsync(organizationId, cancellationToken);
        return BackofficeOrganizationDetailsDto.FromData(data);
    }

    public async Task<BackofficeUserListResponse> GetUsersAsync(
        Guid organizationId,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var organization = await GetRequiredOrganizationAsync(organizationId, cancellationToken);
        var members = await memberQueries.GetMembers(organizationId, cancellationToken);
        var normalizedSearch = search?.Trim();

        var items = members
            .Where(member => string.IsNullOrWhiteSpace(normalizedSearch)
                || member.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                || member.Email.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
            .OrderBy(member => member.Name)
            .ThenBy(member => member.Email)
            .Select(member => ToSummary(member, organization))
            .ToArray();

        return new BackofficeUserListResponse(items);
    }

    public async Task<BackofficeUserDetailsDto> GetUserDetailsAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var organization = await GetRequiredOrganizationAsync(organizationId, cancellationToken);
        var member = await GetRequiredMemberAsync(organizationId, userId, cancellationToken);
        var diagnostic = Diagnose(member, organization);

        return new BackofficeUserDetailsDto(
            member.Id,
            member.OrganizationId,
            member.Name,
            member.Email,
            member.IdentityProvider.ToString(),
            member.ExternalUserId,
            ToRoleLabel(member.Role),
            diagnostic.AccessAllowed ? "Allowed" : "Denied",
            ToOnboardingStatus(organization),
            member.LastSuccessfulAuthenticationAt,
            diagnostic);
    }

    public async Task<BackofficeAccessReevaluationResultDto> ReevaluateUserAccessAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var organization = await GetRequiredOrganizationAsync(organizationId, cancellationToken);
        var member = await GetRequiredMemberAsync(organizationId, userId, cancellationToken);
        var diagnostic = Diagnose(member, organization);
        var correlationId = correlationIdProvider.GetCorrelationId();
        var evaluatedAt = DateTimeOffset.UtcNow;
        var identity = currentIdentity.GetIdentity();

        if (!Guid.TryParse(identity.ExternalUserId, out var actorId))
        {
            throw new InvalidOperationException(
                "The authenticated management user identifier must be a GUID to create an administrative audit entry.");
        }

        var auditEntry = AdministrativeAuditEntryFactory.Create(
            organizationId,
            "ManagementAdmin",
            actorId,
            AdministrativeAuditAction.UserAccessReevaluated,
            "OrganizationMember",
            userId,
            evaluatedAt,
            new Dictionary<string, object?>(),
            new Dictionary<string, object?>
            {
                ["accessAllowed"] = diagnostic.AccessAllowed,
                ["diagnosticCode"] = diagnostic.Code
            },
            correlationId);

        await administrativeAuditRepository.PersistAsync(auditEntry, cancellationToken);

        logger.LogInformation(
            "Backoffice access reevaluation. OrganizationId={OrganizationId} UserId={UserId} AccessAllowed={AccessAllowed} DiagnosticCode={DiagnosticCode} CorrelationId={CorrelationId}",
            organizationId,
            userId,
            diagnostic.AccessAllowed,
            diagnostic.Code,
            correlationId);

        return new BackofficeAccessReevaluationResultDto(
            userId,
            diagnostic,
            correlationId,
            evaluatedAt);
    }

    private async Task<BackofficeOrganizationDetailsData> GetRequiredOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty)
        {
            throw new BadRequestException("Organization identifier is required.");
        }

        var data = await organizationQueries.GetOrganizationDetailsAsync(
            organizationId,
            cancellationToken);

        return data is null
            ? throw new NotFoundException("Organization not found.")
            : data;
    }

    private async Task<OrganizationMember> GetRequiredMemberAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            throw new BadRequestException("User identifier is required.");
        }

        var member = await memberQueries.FindMember(organizationId, userId, cancellationToken);
        return member is null
            ? throw new NotFoundException("User not found in this organization.")
            : member;
    }

    private static BackofficeUserSummaryDto ToSummary(
        OrganizationMember member,
        BackofficeOrganizationDetailsData organization)
    {
        var diagnostic = Diagnose(member, organization);
        return new BackofficeUserSummaryDto(
            member.Id,
            member.Name,
            member.Email,
            ToRoleLabel(member.Role),
            diagnostic.AccessAllowed ? "Allowed" : "Denied",
            ToOnboardingStatus(organization),
            member.LastSuccessfulAuthenticationAt,
            diagnostic);
    }

    private static BackofficeAccessDiagnosticDto Diagnose(
        OrganizationMember member,
        BackofficeOrganizationDetailsData organization)
    {
        var reasons = new List<string>();

        if (organization.Organization.Status != RecordStatus.Active)
        {
            reasons.Add("L’organisation est inactive.");
        }

        if (member.Status != RecordStatus.Active)
        {
            reasons.Add("Le compte utilisateur est inactif.");
        }

        if (string.IsNullOrWhiteSpace(organization.Organization.TenantId))
        {
            reasons.Add("Aucun tenant Microsoft n’est associé à l’organisation.");
        }

        if (!organization.Microsoft.AdminConsentGranted)
        {
            reasons.Add("L’onboarding Microsoft 365 est incomplet : le consentement administrateur est manquant.");
        }

        if (reasons.Count == 0)
        {
            return new BackofficeAccessDiagnosticDto(
                true,
                "AccessAllowed",
                "Aucun blocage d’accès connu n’a été détecté.",
                Array.Empty<string>());
        }

        var code = organization.Organization.Status != RecordStatus.Active
            ? "OrganizationInactive"
            : member.Status != RecordStatus.Active
                ? "UserInactive"
                : string.IsNullOrWhiteSpace(organization.Organization.TenantId)
                    ? "TenantNotConfigured"
                    : "OnboardingIncomplete";

        return new BackofficeAccessDiagnosticDto(
            false,
            code,
            "Un ou plusieurs blocages d’accès ont été détectés.",
            reasons);
    }

    private static string ToRoleLabel(OrganizationRole role) => role switch
    {
        OrganizationRole.Admin => "TenantAdmin",
        _ => "User"
    };

    private static string ToOnboardingStatus(BackofficeOrganizationDetailsData organization) =>
        organization.Microsoft.AdminConsentGranted ? "Complete" : "Incomplete";
}
