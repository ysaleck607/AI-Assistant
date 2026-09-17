using AssistantCore.Repository.Abstractions;
using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Service.Application.Services.AuthenticateUser;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Authentication;
using AssistantCore.Service.Application.Services.TenantAdmission;

namespace AssistantCore.Service.Tests.Authentication;

public sealed class AuthenticateUserServiceTests
{
    [Theory, AutoDomainData]
    public async Task Given_AnExistingActiveMember_When_GetOrganizationAsync_Then_ReturnsOrganizationAndMember(
        CancellationToken cancellationToken,
        Organization organization,
        OrganizationMember member,
        AuthenticatedIdentity authenticatedIdentity)
    {
        // Given
        member.OrganizationId = organization.Id;
        member.Status = RecordStatus.Active;
        var identity = new StubCurrentIdentity { Identity = authenticatedIdentity };
        var organizationQueries = new StubOrganizationQueries { Result = organization };
        var memberQueries = new StubOrganizationMemberQueries { FoundMember = member };
        var timeProvider = new StubTimeProvider();
        var service = new AuthenticateUserService(
            identity,
            memberQueries,
            organizationQueries,
            new StubOrganizationRepository(),
            new StubOrganizationRoleResolver(),
            new StubMicrosoft365OnboardingCompletionChecker(),
            new TenantAdmissionPolicy(),
            timeProvider);

        // When
        var result = await service.GetOrganizationAsync(cancellationToken);

        // Then
        Assert.Same(organization, result.Organization);
        Assert.Same(member, result.Member);
        Assert.Equal(identity.Identity.Provider, organizationQueries.ReceivedIdentityProvider);
        Assert.Equal(identity.Identity.ExternalOrganizationId, organizationQueries.ReceivedExternalTenantId);
        Assert.Equal(organization.Id, memberQueries.ReceivedOrganizationId);
        Assert.Equal(identity.Identity.ExternalUserId, memberQueries.ReceivedExternalUserId);
        Assert.Equal(cancellationToken, organizationQueries.ReceivedCancellationToken);
        Assert.Equal(cancellationToken, memberQueries.ReceivedCancellationToken);
        Assert.Null(memberQueries.CreatedMember);
        Assert.Equal(1, memberQueries.RecordSuccessfulAuthenticationCallCount);
        Assert.Equal(member.Id, memberQueries.ReceivedAuthenticatedMemberId);
        Assert.Equal(timeProvider.UtcNow, memberQueries.ReceivedAuthenticatedAt);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnExistingMemberWithAStaleNameAndEmail_When_GetOrganizationAsync_Then_RefreshesThemFromTheDirectory(
        Organization organization,
        OrganizationMember member,
        AuthenticatedIdentity authenticatedIdentity)
    {
        // Given
        member.OrganizationId = organization.Id;
        member.Status = RecordStatus.Active;
        member.Name = "Ancien nom";
        member.Email = "ancien-email@contoso.test";
        var memberQueries = new StubOrganizationMemberQueries { FoundMember = member };
        var service = new AuthenticateUserService(
            new StubCurrentIdentity { Identity = authenticatedIdentity },
            memberQueries,
            new StubOrganizationQueries { Result = organization },
            new StubOrganizationRepository(),
            new StubOrganizationRoleResolver(),
            new StubMicrosoft365OnboardingCompletionChecker(),
            new TenantAdmissionPolicy(),
            new StubTimeProvider());

        // When
        var result = await service.GetOrganizationAsync(CancellationToken.None);

        // Then
        Assert.Equal(authenticatedIdentity.DisplayName, result.Member.Name);
        Assert.Equal(authenticatedIdentity.Email, result.Member.Email);
        Assert.Equal(1, memberQueries.RefreshContactDetailsCallCount);
        Assert.Equal(member.Id, memberQueries.ReceivedContactDetailsMemberId);
        Assert.Equal(authenticatedIdentity.DisplayName, memberQueries.ReceivedName);
        Assert.Equal(authenticatedIdentity.Email, memberQueries.ReceivedEmail);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnUnknownOrganization_When_GetOrganizationAsync_Then_ThrowsForbiddenException(
        AuthenticatedIdentity authenticatedIdentity)
    {
        // Given
        var memberQueries = new StubOrganizationMemberQueries();
        var service = new AuthenticateUserService(
            new StubCurrentIdentity { Identity = authenticatedIdentity },
            memberQueries,
            new StubOrganizationQueries(),
            new StubOrganizationRepository(),
            new StubOrganizationRoleResolver(),
            new StubMicrosoft365OnboardingCompletionChecker(),
            new TenantAdmissionPolicy(),
            new StubTimeProvider());

        // When
        var exception = await Assert.ThrowsAsync<ForbiddenException>(
            () => service.GetOrganizationAsync(CancellationToken.None));

        // Then
        Assert.Equal(
            $"No active organization is registered for tenant '{authenticatedIdentity.ExternalOrganizationId}'.",
            exception.Message);
        Assert.Equal(0, memberQueries.FindMemberCallCount);
        Assert.Equal(0, memberQueries.RecordSuccessfulAuthenticationCallCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_NoExistingMember_When_GetOrganizationAsync_Then_CreatesAnActiveUser(
        CancellationToken cancellationToken,
        Organization organization,
        AuthenticatedIdentity authenticatedIdentity)
    {
        // Given
        var identity = new StubCurrentIdentity
        {
            Identity = authenticatedIdentity
        };
        var organizationQueries = new StubOrganizationQueries { Result = organization };
        var memberQueries = new StubOrganizationMemberQueries();
        var timeProvider = new StubTimeProvider();
        var service = new AuthenticateUserService(
            identity,
            memberQueries,
            organizationQueries,
            new StubOrganizationRepository(),
            new StubOrganizationRoleResolver(),
            new StubMicrosoft365OnboardingCompletionChecker(),
            new TenantAdmissionPolicy(),
            timeProvider);

        // When
        var result = await service.GetOrganizationAsync(cancellationToken);

        // Then
        var createdMember = Assert.IsType<OrganizationMember>(memberQueries.CreatedMember);
        Assert.Same(createdMember, result.Member);
        Assert.NotEqual(Guid.Empty, createdMember.Id);
        Assert.Equal(organization.Id, createdMember.OrganizationId);
        Assert.Equal(authenticatedIdentity.DisplayName, createdMember.Name);
        Assert.Equal(authenticatedIdentity.Email, createdMember.Email);
        Assert.Equal(authenticatedIdentity.Provider, createdMember.IdentityProvider);
        Assert.Equal(authenticatedIdentity.ExternalUserId, createdMember.ExternalUserId);
        Assert.Equal(OrganizationRole.User, createdMember.Role);
        Assert.Equal(RecordStatus.Active, createdMember.Status);
        Assert.Equal(cancellationToken, memberQueries.ReceivedCancellationToken);
        Assert.Equal(1, memberQueries.RecordSuccessfulAuthenticationCallCount);
        Assert.Equal(createdMember.Id, memberQueries.ReceivedAuthenticatedMemberId);
        Assert.Equal(timeProvider.UtcNow, memberQueries.ReceivedAuthenticatedAt);
    }

    [Theory, AutoDomainData]
    public async Task Given_NoDisplayName_When_GetOrganizationAsync_Then_UsesEmailAsDisplayName(
        Organization organization,
        AuthenticatedIdentity authenticatedIdentity)
    {
        // Given
        var identityWithoutDisplayName = authenticatedIdentity with { DisplayName = null };
        var identity = new StubCurrentIdentity
        {
            Identity = identityWithoutDisplayName
        };
        var memberQueries = new StubOrganizationMemberQueries();
        var service = new AuthenticateUserService(
            identity,
            memberQueries,
            new StubOrganizationQueries { Result = organization },
            new StubOrganizationRepository(),
            new StubOrganizationRoleResolver(),
            new StubMicrosoft365OnboardingCompletionChecker(),
            new TenantAdmissionPolicy(),
            new StubTimeProvider());

        // When
        await service.GetOrganizationAsync(CancellationToken.None);

        // Then
        Assert.Equal(identityWithoutDisplayName.Email, memberQueries.CreatedMember?.Name);
    }

    [Theory, AutoDomainData]
    public async Task Given_NoEmail_When_GetOrganizationAsync_Then_ThrowsUnauthorizedException(
        Organization organization,
        AuthenticatedIdentity authenticatedIdentity)
    {
        // Given
        var memberQueries = new StubOrganizationMemberQueries();
        var service = new AuthenticateUserService(
            new StubCurrentIdentity
            {
                Identity = authenticatedIdentity with { Email = null }
            },
            memberQueries,
            new StubOrganizationQueries { Result = organization },
            new StubOrganizationRepository(),
            new StubOrganizationRoleResolver(),
            new StubMicrosoft365OnboardingCompletionChecker(),
            new TenantAdmissionPolicy(),
            new StubTimeProvider());

        // When
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.GetOrganizationAsync(CancellationToken.None));

        // Then
        Assert.Equal("Authenticated user email is missing.", exception.Message);
        Assert.Null(memberQueries.CreatedMember);
        Assert.Equal(0, memberQueries.RecordSuccessfulAuthenticationCallCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnInactiveMember_When_GetOrganizationAsync_Then_ThrowsForbiddenException(
        Organization organization,
        OrganizationMember member,
        AuthenticatedIdentity authenticatedIdentity)
    {
        // Given
        member.OrganizationId = organization.Id;
        member.Status = RecordStatus.Inactive;
        var memberQueries = new StubOrganizationMemberQueries { FoundMember = member };
        var service = new AuthenticateUserService(
            new StubCurrentIdentity { Identity = authenticatedIdentity },
            memberQueries,
            new StubOrganizationQueries { Result = organization },
            new StubOrganizationRepository(),
            new StubOrganizationRoleResolver(),
            new StubMicrosoft365OnboardingCompletionChecker(),
            new TenantAdmissionPolicy(),
            new StubTimeProvider());

        // When
        var exception = await Assert.ThrowsAsync<ForbiddenException>(
            () => service.GetOrganizationAsync(CancellationToken.None));

        // Then
        Assert.Equal("Organization member access denied.", exception.Message);
        Assert.Equal(0, memberQueries.RecordSuccessfulAuthenticationCallCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnOrganizationRegisteredByDomain_When_GetOrganizationAsync_Then_AssociatesTenantAndReturnsOrganization(
        CancellationToken cancellationToken,
        Organization organization,
        AuthenticatedIdentity authenticatedIdentity)
    {
        // Given
        organization.Domain = "contoso.com";
        organization.ExternalTenantId = null;
        var identity = authenticatedIdentity with { Email = "admin@contoso.com" };
        var organizationQueries = new StubOrganizationQueries { DomainResult = organization };
        var organizationRepository = new StubOrganizationRepository { AssociatedOrganization = organization };
        var service = new AuthenticateUserService(
            new StubCurrentIdentity { Identity = identity },
            new StubOrganizationMemberQueries(),
            organizationQueries,
            organizationRepository,
            new StubOrganizationRoleResolver(),
            new StubMicrosoft365OnboardingCompletionChecker(),
            new TenantAdmissionPolicy(),
            new StubTimeProvider());

        // When
        var result = await service.GetOrganizationAsync(cancellationToken);

        // Then
        Assert.Same(organization, result.Organization);
        Assert.Equal("contoso.com", organizationQueries.ReceivedDomain);
        Assert.Equal(organization.Id, organizationRepository.ReceivedAssociationOrganizationId);
        Assert.Equal(identity.Provider, organizationRepository.ReceivedAssociationIdentityProvider);
        Assert.Equal(identity.ExternalOrganizationId, organizationRepository.ReceivedAssociationExternalTenantId);
    }

    [Theory, AutoDomainData]
    public async Task Given_NoExistingMember_When_GetOrganizationAsync_Then_CreatesAnActiveAdminWhenResolverReturnsAdmin(
        Organization organization,
        AuthenticatedIdentity authenticatedIdentity)
    {
        // Given
        var identity = new StubCurrentIdentity { Identity = authenticatedIdentity };
        var organizationQueries = new StubOrganizationQueries { Result = organization };
        var memberQueries = new StubOrganizationMemberQueries();
        var roleResolver = new StubOrganizationRoleResolver { Role = OrganizationRole.Admin };
        var service = new AuthenticateUserService(
            identity,
            memberQueries,
            organizationQueries,
            new StubOrganizationRepository(),
            roleResolver,
            new StubMicrosoft365OnboardingCompletionChecker(),
            new TenantAdmissionPolicy(),
            new StubTimeProvider());

        // When
        await service.GetOrganizationAsync(CancellationToken.None);

        // Then
        var createdMember = Assert.IsType<OrganizationMember>(memberQueries.CreatedMember);
        Assert.Equal(OrganizationRole.Admin, createdMember.Role);
        Assert.Same(authenticatedIdentity.AppRoles, roleResolver.ReceivedAppRoles);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnExistingUser_When_GetOrganizationAsync_Then_UsesTokenAdminRoleWithoutPersistingIt(
        Organization organization,
        OrganizationMember member,
        AuthenticatedIdentity authenticatedIdentity)
    {
        // Given
        member.OrganizationId = organization.Id;
        member.Status = RecordStatus.Active;
        member.Role = OrganizationRole.User;
        var memberQueries = new StubOrganizationMemberQueries { FoundMember = member };
        var roleResolver = new StubOrganizationRoleResolver { Role = OrganizationRole.Admin };
        var service = new AuthenticateUserService(
            new StubCurrentIdentity { Identity = authenticatedIdentity },
            memberQueries,
            new StubOrganizationQueries { Result = organization },
            new StubOrganizationRepository(),
            roleResolver,
            new StubMicrosoft365OnboardingCompletionChecker(),
            new TenantAdmissionPolicy(),
            new StubTimeProvider());

        // When
        var result = await service.GetOrganizationAsync(CancellationToken.None);

        // Then
        Assert.Equal(0, memberQueries.UpdateRoleCallCount);
        Assert.Equal(OrganizationRole.Admin, result.Member.Role);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnExistingAdmin_When_GetOrganizationAsync_Then_UsesTokenUserRoleWithoutPersistingIt(
        Organization organization,
        OrganizationMember member,
        AuthenticatedIdentity authenticatedIdentity)
    {
        // Given
        member.OrganizationId = organization.Id;
        member.Status = RecordStatus.Active;
        member.Role = OrganizationRole.Admin;
        var memberQueries = new StubOrganizationMemberQueries { FoundMember = member };
        var roleResolver = new StubOrganizationRoleResolver { Role = OrganizationRole.User };
        var service = new AuthenticateUserService(
            new StubCurrentIdentity { Identity = authenticatedIdentity },
            memberQueries,
            new StubOrganizationQueries { Result = organization },
            new StubOrganizationRepository(),
            roleResolver,
            new StubMicrosoft365OnboardingCompletionChecker(),
            new TenantAdmissionPolicy(),
            new StubTimeProvider());

        // When
        var result = await service.GetOrganizationAsync(CancellationToken.None);

        // Then
        Assert.Equal(0, memberQueries.UpdateRoleCallCount);
        Assert.Equal(OrganizationRole.User, result.Member.Role);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnExistingMemberWithTheSameResolvedRole_When_GetOrganizationAsync_Then_DoesNotWriteToTheDatabase(
        Organization organization,
        OrganizationMember member,
        AuthenticatedIdentity authenticatedIdentity)
    {
        // Given
        member.OrganizationId = organization.Id;
        member.Status = RecordStatus.Active;
        member.Role = OrganizationRole.Admin;
        var memberQueries = new StubOrganizationMemberQueries { FoundMember = member };
        var roleResolver = new StubOrganizationRoleResolver { Role = OrganizationRole.Admin };
        var service = new AuthenticateUserService(
            new StubCurrentIdentity { Identity = authenticatedIdentity },
            memberQueries,
            new StubOrganizationQueries { Result = organization },
            new StubOrganizationRepository(),
            roleResolver,
            new StubMicrosoft365OnboardingCompletionChecker(),
            new TenantAdmissionPolicy(),
            new StubTimeProvider());

        // When
        await service.GetOrganizationAsync(CancellationToken.None);

        // Then
        Assert.Equal(0, memberQueries.UpdateRoleCallCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnIncompleteSetupAndAStandardUser_When_GetOrganizationAsync_Then_ThrowsTenantAdmissionException(
        Organization organization,
        OrganizationMember member,
        AuthenticatedIdentity authenticatedIdentity)
    {
        // Given
        member.OrganizationId = organization.Id;
        member.Status = RecordStatus.Active;
        member.Role = OrganizationRole.Admin;
        var memberQueries = new StubOrganizationMemberQueries { FoundMember = member };
        var onboardingCompletionChecker = new StubMicrosoft365OnboardingCompletionChecker
        {
            IsComplete = false
        };
        var service = new AuthenticateUserService(
            new StubCurrentIdentity { Identity = authenticatedIdentity },
            memberQueries,
            new StubOrganizationQueries { Result = organization },
            new StubOrganizationRepository(),
            new StubOrganizationRoleResolver { Role = OrganizationRole.User },
            onboardingCompletionChecker,
            new TenantAdmissionPolicy(),
            new StubTimeProvider());

        // When
        var exception = await Assert.ThrowsAsync<TenantAdmissionException>(() =>
            service.GetOrganizationAsync(CancellationToken.None));

        // Then
        Assert.Equal(TenantAdmissionException.TenantAdminRequired, exception.ErrorCode);
        Assert.Equal(organization.Id, onboardingCompletionChecker.ReceivedOrganizationId);
        Assert.Equal(0, memberQueries.RecordSuccessfulAuthenticationCallCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnIncompleteSetupAndATenantAdmin_When_GetOrganizationAsync_Then_Succeeds(
        Organization organization,
        OrganizationMember member,
        AuthenticatedIdentity authenticatedIdentity)
    {
        // Given
        member.OrganizationId = organization.Id;
        member.Status = RecordStatus.Active;
        member.Role = OrganizationRole.User;
        var memberQueries = new StubOrganizationMemberQueries { FoundMember = member };
        var service = new AuthenticateUserService(
            new StubCurrentIdentity { Identity = authenticatedIdentity },
            memberQueries,
            new StubOrganizationQueries { Result = organization },
            new StubOrganizationRepository(),
            new StubOrganizationRoleResolver { Role = OrganizationRole.Admin },
            new StubMicrosoft365OnboardingCompletionChecker { IsComplete = false },
            new TenantAdmissionPolicy(),
            new StubTimeProvider());

        // When
        var result = await service.GetOrganizationAsync(CancellationToken.None);

        // Then
        Assert.Same(member, result.Member);
        Assert.Equal(OrganizationRole.Admin, result.Member.Role);
        Assert.Equal(1, memberQueries.RecordSuccessfulAuthenticationCallCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_ACompleteSetupAndAStandardUser_When_GetOrganizationAsync_Then_Succeeds(
        Organization organization,
        OrganizationMember member,
        AuthenticatedIdentity authenticatedIdentity)
    {
        // Given
        member.OrganizationId = organization.Id;
        member.Status = RecordStatus.Active;
        member.Role = OrganizationRole.Admin;
        var memberQueries = new StubOrganizationMemberQueries { FoundMember = member };
        var service = new AuthenticateUserService(
            new StubCurrentIdentity { Identity = authenticatedIdentity },
            memberQueries,
            new StubOrganizationQueries { Result = organization },
            new StubOrganizationRepository(),
            new StubOrganizationRoleResolver { Role = OrganizationRole.User },
            new StubMicrosoft365OnboardingCompletionChecker { IsComplete = true },
            new TenantAdmissionPolicy(),
            new StubTimeProvider());

        // When
        var result = await service.GetOrganizationAsync(CancellationToken.None);

        // Then
        Assert.Same(member, result.Member);
        Assert.Equal(OrganizationRole.User, result.Member.Role);
        Assert.Equal(1, memberQueries.RecordSuccessfulAuthenticationCallCount);
    }
}
