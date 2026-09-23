using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Domain.Entities;

namespace AssistantCore.Repository.Queries;

public sealed record OrganizationMemberWithOrganization(
    Organization Organization,
    OrganizationMember Member);

public interface IOrganizationMemberQueries
{
    Task<IReadOnlyCollection<OrganizationMember>> GetMembers(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<OrganizationMemberWithOrganization?> FindMemberWithOrganization(
        IdentityProvider identityProvider,
        string externalOrganizationId,
        string externalUserId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<OrganizationMemberWithOrganization?>(null);

    Task<OrganizationMember?> FindMember(
        Guid organizationId,
        IdentityProvider identityProvider,
        string externalUserId,
        CancellationToken cancellationToken = default);

    Task<OrganizationMember?> FindMember(
        Guid organizationId,
        Guid memberId,
        CancellationToken cancellationToken = default);

    Task<OrganizationMember> CreateMember(
        OrganizationMember member,
        CancellationToken cancellationToken = default);

    Task<OrganizationMember> UpdateRole(
        OrganizationMember member,
        OrganizationRole role,
        Guid actorId,
        DateTimeOffset occurredAt,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<MemberUpdateResult> UpdateStatus(
        Guid organizationId,
        Guid memberId,
        RecordStatus status,
        int? expectedVersion,
        Guid actorId,
        DateTimeOffset occurredAt,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task RecordSuccessfulAuthenticationAsync(
        Guid memberId,
        DateTimeOffset authenticatedAt,
        CancellationToken cancellationToken = default);

    Task RefreshContactDetailsAsync(
        Guid memberId,
        string name,
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Force le rechiffrement de Name/Email et le recalcul de l'index aveugle pour un
    /// lot de membres. A appeler explicitement lors du rattrapage des lignes ecrites
    /// avant l'activation du chiffrement ; jamais declenche automatiquement.
    /// </summary>
    Task<int> ReencryptAllMembersAsync(
        int batchSize,
        CancellationToken cancellationToken = default);
}
