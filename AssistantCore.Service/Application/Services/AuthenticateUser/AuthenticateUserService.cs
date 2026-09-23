using AssistantCore.Repository.Abstractions;
using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Queries;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Authentication;
using AssistantCore.Service.Application.Services.Microsoft365;
using AssistantCore.Service.Application.Services.TenantAdmission;

namespace AssistantCore.Service.Application.Services.AuthenticateUser;

public sealed class AuthenticateUserService(
    ICurrentIdentity currentIdentity,
    IOrganizationMemberQueries organizationMemberQueries,
    IOrganizationQueries organizationQueries,
    IOrganizationRepository organizationRepository,
    IOrganizationRoleResolver organizationRoleResolver,
    IMicrosoft365OnboardingCompletionChecker onboardingCompletionChecker,
    ITenantAdmissionPolicy tenantAdmissionPolicy,
    TimeProvider timeProvider) : IAuthenticateUserService
{
    private const int InitialMemberVersion = 1;

    public async Task<(Organization Organization, OrganizationMember Member)> GetOrganizationAsync(CancellationToken cancellationToken)
    {
        var identity = currentIdentity.GetIdentity();
        var organization = await organizationQueries.FindOrganization(
            identity.Provider,
            identity.ExternalOrganizationId,
            cancellationToken)
            ?? await ResolveOrganizationFromEmailDomainAsync(identity, cancellationToken)
            ?? throw new ForbiddenException(
                $"No active organization is registered for tenant '{identity.ExternalOrganizationId}'.");

        var member = await GetOrCreateActiveMemberAsync(
            organization.Id,
            identity,
            cancellationToken);

        return (organization, member);
    }

    private async Task<Organization?> ResolveOrganizationFromEmailDomainAsync(
        AuthenticatedIdentity identity,
        CancellationToken cancellationToken)
    {
        var email = ResolveEmail(identity);
        var separatorIndex = email.LastIndexOf('@');
        if (separatorIndex < 0 || separatorIndex == email.Length - 1)
        {
            return null;
        }

        var domain = email[(separatorIndex + 1)..].Trim().ToLowerInvariant();
        var organization = await organizationQueries.FindOrganizationByDomain(
            identity.Provider,
            domain,
            cancellationToken);

        if (organization is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(organization.ExternalTenantId))
        {
            return organization;
        }

        return await organizationRepository.AssociateExternalTenantIdAsync(
            organization.Id,
            identity.Provider,
            identity.ExternalOrganizationId,
            cancellationToken);
    }

    private async Task<OrganizationMember> GetOrCreateActiveMemberAsync(
        Guid organizationId,
        AuthenticatedIdentity identity,
        CancellationToken cancellationToken)
    {
        var resolvedRole = organizationRoleResolver.Resolve(identity.AppRoles);
        var member = await organizationMemberQueries.FindMember(
            organizationId,
            identity.Provider,
            identity.ExternalUserId,
            cancellationToken);

        if (member is null)
        {
            member = await organizationMemberQueries.CreateMember(
                new OrganizationMember
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    Name = ResolveDisplayName(identity),
                    Email = ResolveEmail(identity),
                    IdentityProvider = identity.Provider,
                    ExternalUserId = identity.ExternalUserId,
                    Role = resolvedRole,
                    Status = RecordStatus.Active,
                    Version = InitialMemberVersion
                },
                cancellationToken);
        }

        if (member.Status != RecordStatus.Active)
        {
            throw new ForbiddenException("Organization member access denied.");
        }

        // Le rôle persistant est informatif. Les autorisations de la session
        // courante suivent toujours les app roles du JWT.
        member.Role = resolvedRole;

        var isOnboardingComplete = await onboardingCompletionChecker.IsCompleteAsync(
            organizationId,
            cancellationToken);
        var admissionResult = tenantAdmissionPolicy.Evaluate(member.Role, isOnboardingComplete);

        if (admissionResult != TenantAdmissionResult.Allowed)
        {
            throw new TenantAdmissionException(
                "A tenant administrator must finish the Microsoft 365 setup before other members can access AssistantCore.",
                TenantAdmissionException.TenantAdminRequired);
        }

        // The directory is the source of truth for contact details, which never affect
        // authorization: an already-created guest or member whose name or email changed
        // since their previous sign-in must not be left with a stale local copy. A display
        // name claim missing this time (some token types omit it) is not a change to
        // "no name" - it must never erase a name already on file.
        member.Name = string.IsNullOrWhiteSpace(identity.DisplayName) ? member.Name : identity.DisplayName;
        member.Email = ResolveEmail(identity);
        await organizationMemberQueries.RefreshContactDetailsAsync(
            member.Id,
            member.Name,
            member.Email,
            cancellationToken);

        await organizationMemberQueries.RecordSuccessfulAuthenticationAsync(
            member.Id,
            timeProvider.GetUtcNow(),
            cancellationToken);

        return member;
    }

    private static string ResolveEmail(AuthenticatedIdentity identity) =>
        !string.IsNullOrWhiteSpace(identity.Email)
            ? identity.Email
            : throw new UnauthorizedAccessException("Authenticated user email is missing.");

    private static string ResolveDisplayName(AuthenticatedIdentity identity)
    {
        if (!string.IsNullOrWhiteSpace(identity.DisplayName))
        {
            return identity.DisplayName;
        }

        return ResolveEmail(identity);
    }
}
