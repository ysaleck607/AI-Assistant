using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using AssistantCore.Repository.Queries;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Service.Tests.Repository;

public sealed class OrganizationMemberQueriesGetMembersTests
{
    [Fact]
    public async Task Given_MembersFromMultipleOrganizations_When_GetMembers_Then_FiltersAndOrdersMembers()
    {
        // Given
        var currentOrganizationId = Guid.NewGuid();
        var otherOrganizationId = Guid.NewGuid();
        var expectedFirst = CreateMember(
            currentOrganizationId,
            "Alice Martin",
            "alice.a@example.com",
            RecordStatus.Active);
        var expectedSecond = CreateMember(
            currentOrganizationId,
            "Alice Martin",
            "alice.z@example.com",
            RecordStatus.Inactive);
        var expectedThird = CreateMember(
            currentOrganizationId,
            "Zoe Roy",
            "zoe@example.com",
            RecordStatus.Active);
        var otherOrganizationMember = CreateMember(
            otherOrganizationId,
            "Aaron Other",
            "aaron@example.com",
            RecordStatus.Active);
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new AssistantCoreDbContext(options);
        dbContext.OrganizationMembers.AddRange(
            expectedThird,
            otherOrganizationMember,
            expectedSecond,
            expectedFirst);
        await dbContext.SaveChangesAsync();
        var queries = new OrganizationMemberQueries(dbContext, new StubAdministrativeAuditRepository(), new StubEmailBlindIndexHasher());

        // When
        var result = await queries.GetMembers(currentOrganizationId, CancellationToken.None);

        // Then
        Assert.Equal(
            [expectedFirst.Id, expectedSecond.Id, expectedThird.Id],
            result.Select(member => member.Id));
        Assert.Contains(result, member => member.Status == RecordStatus.Active);
        Assert.Contains(result, member => member.Status == RecordStatus.Inactive);
        Assert.DoesNotContain(result, member => member.Id == otherOrganizationMember.Id);
    }

    private static OrganizationMember CreateMember(
        Guid organizationId,
        string name,
        string email,
        RecordStatus status) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        Name = name,
        Email = email,
        IdentityProvider = IdentityProvider.MicrosoftEntraId,
        ExternalUserId = Guid.NewGuid().ToString(),
        Role = OrganizationRole.User,
        Status = status
    };
}
