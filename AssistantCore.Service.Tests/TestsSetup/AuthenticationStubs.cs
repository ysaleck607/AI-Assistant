using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Queries;
using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Authentication;
using AssistantCore.Service.Application.Services.AuthenticateUser;
using AssistantCore.Service.Application.Services.Microsoft365;

namespace AssistantCore.Service.Tests;

internal sealed class StubCurrentIdentity : ICurrentIdentity
{
    public AuthenticatedIdentity Identity { get; init; } = new(
        IdentityProvider.MicrosoftEntraId,
        "tenant-id",
        "user-id",
        "Test User",
        "test.user@example.com",
        []);

    public AuthenticatedIdentity GetIdentity() => Identity;
}

internal sealed class StubMicrosoft365OnboardingCompletionChecker : IMicrosoft365OnboardingCompletionChecker
{
    public bool IsComplete { get; set; } = true;

    public Guid? ReceivedOrganizationId { get; private set; }

    public Task<bool> IsCompleteAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        ReceivedOrganizationId = organizationId;
        return Task.FromResult(IsComplete);
    }
}

internal sealed class StubOrganizationRoleResolver : IOrganizationRoleResolver
{
    public OrganizationRole Role { get; set; } = OrganizationRole.User;

    public Exception? Exception { get; set; }

    public IReadOnlyCollection<string>? ReceivedAppRoles { get; private set; }

    public OrganizationRole Resolve(IReadOnlyCollection<string> appRoles)
    {
        ReceivedAppRoles = appRoles;

        if (Exception is not null)
        {
            throw Exception;
        }

        return Role;
    }
}

internal sealed class StubOrganizationQueries : IOrganizationQueries
{
    public Organization? Result { get; set; }

    public Organization? DomainResult { get; set; }

    public IdentityProvider? ReceivedIdentityProvider { get; private set; }

    public string? ReceivedExternalTenantId { get; private set; }

    public string? ReceivedDomain { get; private set; }

    public CancellationToken ReceivedCancellationToken { get; private set; }

    public Task<Organization?> FindOrganization(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        ReceivedCancellationToken = cancellationToken;
        return Task.FromResult(Result?.Id == organizationId ? Result : null);
    }

    public Task<Organization?> FindOrganization(
        IdentityProvider identityProvider,
        string externalTenantId,
        CancellationToken cancellationToken = default)
    {
        ReceivedIdentityProvider = identityProvider;
        ReceivedExternalTenantId = externalTenantId;
        ReceivedCancellationToken = cancellationToken;
        return Task.FromResult(Result);
    }

    public Task<Organization?> FindOrganizationByDomain(
        IdentityProvider identityProvider,
        string domain,
        CancellationToken cancellationToken = default)
    {
        ReceivedIdentityProvider = identityProvider;
        ReceivedDomain = domain;
        ReceivedCancellationToken = cancellationToken;
        return Task.FromResult(DomainResult);
    }
}

internal sealed class StubOrganizationMemberQueries : IOrganizationMemberQueries
{
    public IReadOnlyCollection<OrganizationMember> Members { get; init; } = [];

    public OrganizationMember? FoundMember { get; set; }

    public OrganizationMember? CreatedMember { get; private set; }

    public OrganizationMember? UpdatedMember { get; private set; }

    public Guid? ReceivedOrganizationId { get; private set; }

    public Guid? ReceivedMemberId { get; private set; }

    public IdentityProvider? ReceivedIdentityProvider { get; private set; }

    public string? ReceivedExternalUserId { get; private set; }

    public OrganizationRole? ReceivedRole { get; private set; }

    public CancellationToken ReceivedCancellationToken { get; private set; }

    public int FindMemberCallCount { get; private set; }

    public int GetMembersCallCount { get; private set; }

    public int FindMemberByIdCallCount { get; private set; }

    public int UpdateRoleCallCount { get; private set; }

    public int UpdateStatusCallCount { get; private set; }

    public RecordStatus? ReceivedStatus { get; private set; }

    public int? ReceivedExpectedVersion { get; private set; }

    public MemberUpdateResult UpdateStatusResult { get; set; } =
        MemberUpdateResult.NotFound;

    public int RecordSuccessfulAuthenticationCallCount { get; private set; }

    public Guid? ReceivedAuthenticatedMemberId { get; private set; }

    public DateTimeOffset? ReceivedAuthenticatedAt { get; private set; }

    public Task<OrganizationMember?> FindMember(
        Guid organizationId,
        IdentityProvider identityProvider,
        string externalUserId,
        CancellationToken cancellationToken = default)
    {
        FindMemberCallCount++;
        ReceivedOrganizationId = organizationId;
        ReceivedIdentityProvider = identityProvider;
        ReceivedExternalUserId = externalUserId;
        ReceivedCancellationToken = cancellationToken;
        return Task.FromResult(FoundMember);
    }

    public Task<OrganizationMember> CreateMember(
        OrganizationMember member,
        CancellationToken cancellationToken = default)
    {
        CreatedMember = member;
        ReceivedCancellationToken = cancellationToken;
        return Task.FromResult(member);
    }

    public Task<IReadOnlyCollection<OrganizationMember>> GetMembers(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        GetMembersCallCount++;
        ReceivedOrganizationId = organizationId;
        ReceivedCancellationToken = cancellationToken;
        return Task.FromResult(Members);
    }

    public Task<OrganizationMember?> FindMember(
        Guid organizationId,
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        FindMemberByIdCallCount++;
        ReceivedOrganizationId = organizationId;
        ReceivedMemberId = memberId;
        ReceivedCancellationToken = cancellationToken;
        return Task.FromResult(FoundMember);
    }

    public Guid? ReceivedActorId { get; private set; }

    public DateTimeOffset? ReceivedOccurredAt { get; private set; }

    public string? ReceivedCorrelationId { get; private set; }

    public Task<OrganizationMember> UpdateRole(
        OrganizationMember member,
        OrganizationRole role,
        Guid actorId,
        DateTimeOffset occurredAt,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        UpdateRoleCallCount++;
        UpdatedMember = member;
        ReceivedRole = role;
        ReceivedActorId = actorId;
        ReceivedOccurredAt = occurredAt;
        ReceivedCorrelationId = correlationId;
        ReceivedCancellationToken = cancellationToken;
        member.Role = role;
        return Task.FromResult(member);
    }

    public Task<MemberUpdateResult> UpdateStatus(
        Guid organizationId,
        Guid memberId,
        RecordStatus status,
        int? expectedVersion,
        Guid actorId,
        DateTimeOffset occurredAt,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        UpdateStatusCallCount++;
        ReceivedOrganizationId = organizationId;
        ReceivedMemberId = memberId;
        ReceivedStatus = status;
        ReceivedExpectedVersion = expectedVersion;
        ReceivedActorId = actorId;
        ReceivedOccurredAt = occurredAt;
        ReceivedCorrelationId = correlationId;
        ReceivedCancellationToken = cancellationToken;
        return Task.FromResult(UpdateStatusResult);
    }

    public Task RecordSuccessfulAuthenticationAsync(
        Guid memberId,
        DateTimeOffset authenticatedAt,
        CancellationToken cancellationToken = default)
    {
        RecordSuccessfulAuthenticationCallCount++;
        ReceivedAuthenticatedMemberId = memberId;
        ReceivedAuthenticatedAt = authenticatedAt;
        ReceivedCancellationToken = cancellationToken;
        return Task.CompletedTask;
    }

    public int RefreshContactDetailsCallCount { get; private set; }

    public Guid? ReceivedContactDetailsMemberId { get; private set; }

    public string? ReceivedName { get; private set; }

    public string? ReceivedEmail { get; private set; }

    public Task RefreshContactDetailsAsync(
        Guid memberId,
        string name,
        string email,
        CancellationToken cancellationToken = default)
    {
        RefreshContactDetailsCallCount++;
        ReceivedContactDetailsMemberId = memberId;
        ReceivedName = name;
        ReceivedEmail = email;
        ReceivedCancellationToken = cancellationToken;
        return Task.CompletedTask;
    }
}

internal sealed class StubTimeProvider : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}

internal sealed class StubAuthenticateUserService : IAuthenticateUserService
{
    public required (Organization Organization, OrganizationMember Member) Result { get; init; }

    public CancellationToken ReceivedCancellationToken { get; private set; }

    public Task<(Organization Organization, OrganizationMember Member)> GetOrganizationAsync(
        CancellationToken cancellationToken)
    {
        ReceivedCancellationToken = cancellationToken;
        return Task.FromResult(Result);
    }
}
