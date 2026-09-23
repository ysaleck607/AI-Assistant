using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using AssistantCore.Repository.Repositories;
using AssistantCore.Repository.Repositories.Audit;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Repository.Queries;

public sealed class OrganizationMemberQueries(
    AssistantCoreDbContext dbContext,
    IAdministrativeAuditRepository administrativeAuditRepository,
    IEmailBlindIndexHasher emailHasher) : IOrganizationMemberQueries
{
    public async Task<IReadOnlyCollection<OrganizationMember>> GetMembers(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        // Name and Email are encrypted at rest with a non-deterministic cipher, so an
        // ORDER BY on either can no longer be pushed down to SQL. Ordering happens in
        // memory instead, after decryption, on the organization-scoped candidate set.
        var members = await dbContext.OrganizationMembers
            .AsNoTracking()
            .Where(member => member.OrganizationId == organizationId)
            .ToArrayAsync(cancellationToken);

        return members
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .ThenBy(member => member.Email, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<OrganizationMemberWithOrganization?> FindMemberWithOrganization(
        IdentityProvider identityProvider,
        string externalOrganizationId,
        string externalUserId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.OrganizationMembers
            .AsNoTracking()
            .Where(member =>
                member.IdentityProvider == identityProvider
                && member.ExternalUserId == externalUserId
                && member.Organization.IdentityProvider == identityProvider
                && member.Organization.ExternalTenantId == externalOrganizationId)
            .Select(member => new OrganizationMemberWithOrganization(
                member.Organization,
                member))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<OrganizationMember?> FindMember(
        Guid organizationId,
        IdentityProvider identityProvider,
        string externalUserId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.OrganizationMembers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                member =>
                    member.OrganizationId == organizationId
                    && member.IdentityProvider == identityProvider
                    && member.ExternalUserId == externalUserId,
                cancellationToken);
    }

    public async Task<OrganizationMember?> FindMember(
        Guid organizationId,
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.OrganizationMembers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                member =>
                    member.OrganizationId == organizationId
                    && member.Id == memberId,
                cancellationToken);
    }

    public async Task<OrganizationMember> CreateMember(
        OrganizationMember member,
        CancellationToken cancellationToken = default)
    {
        member.EmailLookupHash = emailHasher.ComputeHash(member.Email);
        dbContext.OrganizationMembers.Add(member);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return member;
        }
        catch (DbUpdateException exception) when (IsMemberIdentityConflict(exception))
        {
            dbContext.Entry(member).State = EntityState.Detached;

            var competingMember = await FindMember(
                member.OrganizationId,
                member.IdentityProvider,
                member.ExternalUserId,
                cancellationToken);

            return competingMember
                ?? throw new InvalidOperationException(
                    "The member created by the competing request could not be reloaded.");
        }
    }

    private static bool IsMemberIdentityConflict(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;

        return message.Contains(
                   "IX_OrganizationMember_OrganizationId_IdentityProvider_ExternalUserId",
                   StringComparison.OrdinalIgnoreCase)
               || (message.Contains(nameof(OrganizationMember.OrganizationId), StringComparison.OrdinalIgnoreCase)
                   && message.Contains(nameof(OrganizationMember.IdentityProvider), StringComparison.OrdinalIgnoreCase)
                   && message.Contains(nameof(OrganizationMember.ExternalUserId), StringComparison.OrdinalIgnoreCase));
    }

    public async Task<OrganizationMember> UpdateRole(
        OrganizationMember member,
        OrganizationRole role,
        Guid actorId,
        DateTimeOffset occurredAt,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (member.Role == role)
        {
            return member;
        }

        var previousRole = member.Role;
        dbContext.OrganizationMembers.Attach(member);
        member.Role = role;

        administrativeAuditRepository.Stage(AdministrativeAuditEntryFactory.Create(
            member.OrganizationId,
            AdministrativeAuditSubjectTypes.Member,
            actorId,
            AdministrativeAuditAction.MemberRoleChanged,
            AdministrativeAuditSubjectTypes.Member,
            member.Id,
            occurredAt,
            new Dictionary<string, object?> { ["role"] = previousRole.ToString() },
            new Dictionary<string, object?> { ["role"] = role.ToString() },
            correlationId));

        await dbContext.SaveChangesAsync(cancellationToken);
        return member;
    }

    public async Task<MemberUpdateResult> UpdateStatus(
        Guid organizationId,
        Guid memberId,
        RecordStatus status,
        int? expectedVersion,
        Guid actorId,
        DateTimeOffset occurredAt,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var member = await dbContext.OrganizationMembers
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == memberId
                    && candidate.OrganizationId == organizationId,
                cancellationToken);

        if (member is null)
        {
            return MemberUpdateResult.NotFound;
        }

        if (expectedVersion is not null && member.Version != expectedVersion)
        {
            return MemberUpdateResult.VersionConflict;
        }

        if (member.Status == status)
        {
            return MemberUpdateResult.Updated(member);
        }

        var previousStatus = member.Status;
        member.Status = status;
        member.Version += 1;

        administrativeAuditRepository.Stage(AdministrativeAuditEntryFactory.Create(
            organizationId,
            AdministrativeAuditSubjectTypes.Member,
            actorId,
            AdministrativeAuditAction.MemberStatusChanged,
            AdministrativeAuditSubjectTypes.Member,
            member.Id,
            occurredAt,
            new Dictionary<string, object?> { ["status"] = previousStatus.ToString() },
            new Dictionary<string, object?> { ["status"] = status.ToString() },
            correlationId));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return MemberUpdateResult.VersionConflict;
        }

        return MemberUpdateResult.Updated(member);
    }

    public async Task RecordSuccessfulAuthenticationAsync(
        Guid memberId,
        DateTimeOffset authenticatedAt,
        CancellationToken cancellationToken = default)
    {
        var member = await dbContext.OrganizationMembers
            .SingleOrDefaultAsync(candidate => candidate.Id == memberId, cancellationToken);

        if (member is null || member.LastSuccessfulAuthenticationAt >= authenticatedAt)
        {
            return;
        }

        member.LastSuccessfulAuthenticationAt = authenticatedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RefreshContactDetailsAsync(
        Guid memberId,
        string name,
        string email,
        CancellationToken cancellationToken = default)
    {
        var member = await dbContext.OrganizationMembers
            .SingleOrDefaultAsync(candidate => candidate.Id == memberId, cancellationToken);

        // Identity is always the Object ID, never the name or email, so a mismatch here is
        // never a conflict to resolve - it is simply the directory's current value for a
        // guest or member whose profile changed since their previous sign-in.
        if (member is null
            || (string.Equals(member.Name, name, StringComparison.Ordinal)
                && string.Equals(member.Email, email, StringComparison.Ordinal)))
        {
            return;
        }

        member.Name = name;
        member.Email = email;
        member.EmailLookupHash = emailHasher.ComputeHash(email);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ReencryptAllMembersAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var totalProcessed = 0;
        while (true)
        {
            var batch = await dbContext.OrganizationMembers
                .OrderBy(member => member.Id)
                .Skip(totalProcessed)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var member in batch)
            {
                // Forces the converter to re-run even though the plaintext value did not
                // change - this is what actually encrypts a row written before encryption
                // existed, or re-encrypts under the current key.
                dbContext.Entry(member).Property(candidate => candidate.Name).IsModified = true;
                dbContext.Entry(member).Property(candidate => candidate.Email).IsModified = true;
                member.EmailLookupHash = emailHasher.ComputeHash(member.Email);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            totalProcessed += batch.Count;
        }

        return totalProcessed;
    }
}
