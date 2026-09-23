using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using AssistantCore.Repository.Queries;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Service.Tests.Repository;

public sealed class OrganizationMemberQueriesUpdateRoleTests
{
    private const string CorrelationId = "request-8f812";

    [Fact]
    public async Task Given_TheSameRole_When_UpdateRole_Then_ReturnsWithoutTrackingOrChangingMember()
    {
        // Given
        var member = CreateMember(OrganizationRole.User);
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new AssistantCoreDbContext(options);
        var administrativeAuditRepository = new RecordingAdministrativeAuditRepository();
        var queries = new OrganizationMemberQueries(dbContext, administrativeAuditRepository, new StubEmailBlindIndexHasher());

        // When
        var result = await queries.UpdateRole(
            member,
            OrganizationRole.User,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            CorrelationId,
            CancellationToken.None);

        // Then
        Assert.Same(member, result);
        Assert.Equal(OrganizationRole.User, result.Role);
        Assert.Empty(dbContext.ChangeTracker.Entries());
        Assert.Empty(administrativeAuditRepository.StagedEntries);
    }

    [Fact]
    public async Task Given_AChangedRole_When_UpdateRole_Then_PersistsRoleWithoutChangingOtherFields()
    {
        // Given
        var cancellationToken = new CancellationTokenSource().Token;
        var member = CreateMember(OrganizationRole.User);
        var originalName = member.Name;
        var originalEmail = member.Email;
        var originalStatus = member.Status;
        var actorId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new AssistantCoreDbContext(options);
        dbContext.OrganizationMembers.Add(member);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        var administrativeAuditRepository = new RecordingAdministrativeAuditRepository();
        var queries = new OrganizationMemberQueries(dbContext, administrativeAuditRepository, new StubEmailBlindIndexHasher());

        // When
        var result = await queries.UpdateRole(
            member,
            OrganizationRole.Admin,
            actorId,
            occurredAt,
            CorrelationId,
            cancellationToken);

        // Then
        dbContext.ChangeTracker.Clear();
        var persistedMember = await dbContext.OrganizationMembers.SingleAsync(
            candidate => candidate.Id == member.Id,
            cancellationToken);
        Assert.Same(member, result);
        Assert.Equal(OrganizationRole.Admin, persistedMember.Role);
        Assert.Equal(originalName, persistedMember.Name);
        Assert.Equal(originalEmail, persistedMember.Email);
        Assert.Equal(originalStatus, persistedMember.Status);

        var entry = Assert.Single(administrativeAuditRepository.StagedEntries);
        Assert.Equal(member.OrganizationId, entry.OrganizationId);
        Assert.Equal(AdministrativeAuditAction.MemberRoleChanged, entry.Action);
        Assert.Equal(actorId, entry.ActorId);
        Assert.Equal(member.Id, entry.TargetId);
        Assert.Equal(occurredAt, entry.OccurredAt);
        Assert.Equal(CorrelationId, entry.CorrelationId);
        Assert.Contains("\"role\":\"User\"", entry.OldValues);
        Assert.Contains("\"role\":\"Admin\"", entry.NewValues);
    }

    private static OrganizationMember CreateMember(OrganizationRole role) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = Guid.NewGuid(),
        Name = "Target Member",
        Email = "target@example.com",
        IdentityProvider = IdentityProvider.MicrosoftEntraId,
        ExternalUserId = Guid.NewGuid().ToString(),
        Role = role,
        Status = RecordStatus.Active
    };
}
