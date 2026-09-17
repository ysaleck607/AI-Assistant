using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using AssistantCore.Repository.Queries;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Service.Tests.Repository;

public sealed class OrganizationMemberQueriesTests
{
    [Theory, AutoDomainData]
    public async Task Given_ACompetingMemberAlreadyCreated_When_CreateMember_Then_ReturnsTheExistingMember(
        Guid databaseId,
        Guid organizationId,
        string externalUserId)
    {
        // Given
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(databaseId.ToString())
            .Options;

        var winningMember = CreateMember(organizationId, externalUserId, "winning@contoso.test");
        await using (var seedContext = new AssistantCoreDbContext(options))
        {
            seedContext.OrganizationMembers.Add(winningMember);
            await seedContext.SaveChangesAsync();
        }

        var losingMember = CreateMember(organizationId, externalUserId, "losing@contoso.test");
        var identityConflict = CreateDbUpdateException(
            "IX_OrganizationMember_OrganizationId_IdentityProvider_ExternalUserId");

        await using var throwingContext = new ThrowOnceDbContext(options, identityConflict);
        var queries = new OrganizationMemberQueries(throwingContext, new StubAdministrativeAuditRepository());

        // When
        var result = await queries.CreateMember(losingMember, CancellationToken.None);

        // Then
        Assert.Equal(winningMember.Id, result.Id);
        Assert.NotEqual(losingMember.Id, result.Id);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnUnrelatedUniqueConstraintViolation_When_CreateMember_Then_PropagatesTheOriginalException(
        Guid databaseId,
        Guid organizationId,
        string externalUserId)
    {
        // Given
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(databaseId.ToString())
            .Options;

        var member = CreateMember(organizationId, externalUserId, "member@contoso.test");

        // An email conflict mentions OrganizationId, but never IdentityProvider or ExternalUserId.
        var emailConflict = CreateDbUpdateException("IX_OrganizationMember_OrganizationId_Email");

        await using var throwingContext = new ThrowOnceDbContext(options, emailConflict);
        var queries = new OrganizationMemberQueries(throwingContext, new StubAdministrativeAuditRepository());

        // When / Then
        var thrownException = await Assert.ThrowsAsync<DbUpdateException>(
            () => queries.CreateMember(member, CancellationToken.None));
        Assert.Same(emailConflict, thrownException);
    }

    [Theory, AutoDomainData]
    public async Task Given_AGuestAndAnInternalMemberSharingAnEmail_When_CreateMember_Then_BothAreCreatedAsDistinctRows(
        Guid databaseId,
        Guid organizationId,
        string internalMemberExternalUserId,
        string guestExternalUserId,
        string sharedEmail)
    {
        // Given
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(databaseId.ToString())
            .Options;
        var internalMember = CreateMember(organizationId, internalMemberExternalUserId, sharedEmail);
        var guestMember = CreateMember(organizationId, guestExternalUserId, sharedEmail);

        // When
        await using var context = new AssistantCoreDbContext(options);
        var queries = new OrganizationMemberQueries(context, new StubAdministrativeAuditRepository());
        var createdInternalMember = await queries.CreateMember(internalMember, CancellationToken.None);
        var createdGuestMember = await queries.CreateMember(guestMember, CancellationToken.None);

        // Then
        Assert.Equal(internalMember.Id, createdInternalMember.Id);
        Assert.Equal(guestMember.Id, createdGuestMember.Id);
        Assert.NotEqual(createdInternalMember.Id, createdGuestMember.Id);
    }

    private static OrganizationMember CreateMember(Guid organizationId, string externalUserId, string email) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = "Marc Tremblay",
            Email = email,
            IdentityProvider = IdentityProvider.MicrosoftEntraId,
            ExternalUserId = externalUserId,
            Role = OrganizationRole.User,
            Status = RecordStatus.Active
        };

    private static DbUpdateException CreateDbUpdateException(string violatedIndexName) =>
        new(
            "Simulated database conflict.",
            new Exception($"Cannot insert duplicate key row with unique index '{violatedIndexName}'."));

    /// <summary>
    /// Test double that throws a caller-supplied exception on the first
    /// <see cref="SaveChangesAsync"/> call, simulating a concurrent write that
    /// hits a real database constraint, then behaves normally afterwards.
    /// </summary>
    private sealed class ThrowOnceDbContext(
        DbContextOptions<AssistantCoreDbContext> options,
        Exception exceptionToThrowOnce) : AssistantCoreDbContext(options)
    {
        private bool _hasThrown;

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!_hasThrown)
            {
                _hasThrown = true;
                throw exceptionToThrowOnce;
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
