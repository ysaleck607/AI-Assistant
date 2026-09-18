using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using AssistantCore.Repository.Queries;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Service.Tests.Repository;

public sealed class OrganizationMemberLastAuthenticationTests
{
    [Theory, AutoDomainData]
    public async Task Given_ANeverAuthenticatedMember_When_RecordSuccessfulAuthenticationAsync_Then_SetsTheDate(
        Guid databaseId,
        Guid organizationId,
        DateTimeOffset authenticatedAt)
    {
        // Given
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(databaseId.ToString())
            .Options;

        var member = CreateMember(organizationId, lastAuthenticationAt: null);
        await using (var seedContext = new AssistantCoreDbContext(options))
        {
            seedContext.OrganizationMembers.Add(member);
            await seedContext.SaveChangesAsync();
        }

        await using var context = new AssistantCoreDbContext(options);
        var queries = new OrganizationMemberQueries(context, new StubAdministrativeAuditRepository(), new StubEmailBlindIndexHasher());

        // When
        await queries.RecordSuccessfulAuthenticationAsync(member.Id, authenticatedAt, CancellationToken.None);

        // Then
        await using var verificationContext = new AssistantCoreDbContext(options);
        var reloaded = await verificationContext.OrganizationMembers.SingleAsync(m => m.Id == member.Id);
        Assert.Equal(authenticatedAt, reloaded.LastSuccessfulAuthenticationAt);
    }

    [Theory, AutoDomainData]
    public async Task Given_AMoreRecentDateAlreadyStored_When_RecordSuccessfulAuthenticationAsync_Then_DoesNotRegressTheDate(
        Guid databaseId,
        Guid organizationId,
        DateTimeOffset recentAuthenticationAt)
    {
        // Given
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(databaseId.ToString())
            .Options;

        var member = CreateMember(organizationId, lastAuthenticationAt: recentAuthenticationAt);
        await using (var seedContext = new AssistantCoreDbContext(options))
        {
            seedContext.OrganizationMembers.Add(member);
            await seedContext.SaveChangesAsync();
        }

        var olderAttempt = recentAuthenticationAt.AddMinutes(-5);

        await using var context = new AssistantCoreDbContext(options);
        var queries = new OrganizationMemberQueries(context, new StubAdministrativeAuditRepository(), new StubEmailBlindIndexHasher());

        // When
        await queries.RecordSuccessfulAuthenticationAsync(member.Id, olderAttempt, CancellationToken.None);

        // Then
        await using var verificationContext = new AssistantCoreDbContext(options);
        var reloaded = await verificationContext.OrganizationMembers.SingleAsync(m => m.Id == member.Id);
        Assert.Equal(recentAuthenticationAt, reloaded.LastSuccessfulAuthenticationAt);
    }

    private static OrganizationMember CreateMember(Guid organizationId, DateTimeOffset? lastAuthenticationAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = "Marc Tremblay",
            Email = $"{Guid.NewGuid()}@contoso.test",
            IdentityProvider = IdentityProvider.MicrosoftEntraId,
            ExternalUserId = Guid.NewGuid().ToString(),
            Role = OrganizationRole.User,
            Status = RecordStatus.Active,
            LastSuccessfulAuthenticationAt = lastAuthenticationAt
        };
}
