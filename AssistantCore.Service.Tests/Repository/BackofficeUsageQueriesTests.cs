using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using AssistantCore.Repository.Queries;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Service.Tests.Repository;

public sealed class BackofficeUsageQueriesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-22T12:00:00Z");
    private static readonly DateTimeOffset TodayStart = DateTimeOffset.Parse("2026-09-22T00:00:00Z");

    [Theory, AutoDomainData]
    public async Task Given_TwoUsersActiveToday_When_GetSummaryAsync_Then_ComputesDauRequestsAndAverage(
        Guid databaseId)
    {
        // Given
        var organization = CreateOrganization("MetalPro");
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var conversationA = CreateConversation(organization.Id, userA);
        var conversationB = CreateConversation(organization.Id, userB);
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.Add(organization);
        dbContext.Conversations.AddRange(conversationA, conversationB);
        dbContext.Messages.AddRange(
            CreateMessage(conversationA.Id, TodayStart.AddHours(9), MessageRole.User),
            CreateMessage(conversationA.Id, TodayStart.AddHours(10), MessageRole.User),
            CreateMessage(conversationA.Id, TodayStart.AddHours(10), MessageRole.Assistant),
            CreateMessage(conversationB.Id, TodayStart.AddHours(11), MessageRole.User));
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeUsageQueries(dbContext);

        // When
        var summary = await queries.GetSummaryAsync(Now);

        // Then
        Assert.Equal(2, summary.DailyActiveUsers);
        Assert.Equal(3, summary.RequestsToday);
        Assert.Equal(1.5, summary.RequestsPerActiveUser);
    }

    [Theory, AutoDomainData]
    public async Task Given_AUserWithAnOlderMessage_When_GetSummaryAsync_Then_CountsAsReturningNotNew(
        Guid databaseId)
    {
        // Given
        var organization = CreateOrganization("MetalPro");
        var member = Guid.NewGuid();
        var conversation = CreateConversation(organization.Id, member);
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.Add(organization);
        dbContext.Conversations.Add(conversation);
        dbContext.Messages.AddRange(
            CreateMessage(conversation.Id, TodayStart.AddDays(-10), MessageRole.User),
            CreateMessage(conversation.Id, TodayStart.AddHours(9), MessageRole.User));
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeUsageQueries(dbContext);

        // When
        var summary = await queries.GetSummaryAsync(Now);

        // Then
        Assert.Equal(1, summary.DailyActiveUsers);
        Assert.Equal(1, summary.ReturningUsersToday);
        Assert.Equal(0, summary.NewUsersToday);
    }

    [Theory, AutoDomainData]
    public async Task Given_AUserWithOnlyATodayMessage_When_GetSummaryAsync_Then_CountsAsNew(
        Guid databaseId)
    {
        // Given
        var organization = CreateOrganization("MetalPro");
        var member = Guid.NewGuid();
        var conversation = CreateConversation(organization.Id, member);
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.Add(organization);
        dbContext.Conversations.Add(conversation);
        dbContext.Messages.Add(CreateMessage(conversation.Id, TodayStart.AddHours(9), MessageRole.User));
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeUsageQueries(dbContext);

        // When
        var summary = await queries.GetSummaryAsync(Now);

        // Then
        Assert.Equal(1, summary.NewUsersToday);
        Assert.Equal(0, summary.ReturningUsersToday);
    }

    [Theory, AutoDomainData]
    public async Task Given_MessagesAcrossTheLastMonth_When_GetSummaryAsync_Then_ComputesWeeklyAndMonthlyActiveUsers(
        Guid databaseId)
    {
        // Given
        var organization = CreateOrganization("MetalPro");
        var withinWeek = Guid.NewGuid();
        var withinMonthOnly = Guid.NewGuid();
        var tooOld = Guid.NewGuid();
        var conversationWithinWeek = CreateConversation(organization.Id, withinWeek);
        var conversationWithinMonth = CreateConversation(organization.Id, withinMonthOnly);
        var conversationTooOld = CreateConversation(organization.Id, tooOld);
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.Add(organization);
        dbContext.Conversations.AddRange(conversationWithinWeek, conversationWithinMonth, conversationTooOld);
        dbContext.Messages.AddRange(
            CreateMessage(conversationWithinWeek.Id, TodayStart.AddDays(-2), MessageRole.User),
            CreateMessage(conversationWithinMonth.Id, TodayStart.AddDays(-20), MessageRole.User),
            CreateMessage(conversationTooOld.Id, TodayStart.AddDays(-40), MessageRole.User));
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeUsageQueries(dbContext);

        // When
        var summary = await queries.GetSummaryAsync(Now);

        // Then
        Assert.Equal(1, summary.WeeklyActiveUsers);
        Assert.Equal(2, summary.MonthlyActiveUsers);
    }

    [Theory, AutoDomainData]
    public async Task Given_MessagesOnDifferentDays_When_GetDailySeriesAsync_Then_ReturnsOneCorrectPointPerDay(
        Guid databaseId)
    {
        // Given
        var organization = CreateOrganization("MetalPro");
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var conversationA = CreateConversation(organization.Id, userA);
        var conversationB = CreateConversation(organization.Id, userB);
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.Add(organization);
        dbContext.Conversations.AddRange(conversationA, conversationB);
        dbContext.Messages.AddRange(
            CreateMessage(conversationA.Id, TodayStart.AddDays(-1).AddHours(9), MessageRole.User),
            CreateMessage(conversationA.Id, TodayStart.AddDays(-1).AddHours(10), MessageRole.User),
            CreateMessage(conversationB.Id, TodayStart.AddHours(9), MessageRole.User));
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeUsageQueries(dbContext);

        // When
        var points = await queries.GetDailySeriesAsync(Now, days: 2);

        // Then
        Assert.Equal(2, points.Count);
        var yesterday = points[0];
        Assert.Equal(DateOnly.FromDateTime(TodayStart.AddDays(-1).UtcDateTime), yesterday.Date);
        Assert.Equal(1, yesterday.ActiveUsers);
        Assert.Equal(2, yesterday.Requests);
        var today = points[1];
        Assert.Equal(DateOnly.FromDateTime(TodayStart.UtcDateTime), today.Date);
        Assert.Equal(1, today.ActiveUsers);
        Assert.Equal(1, today.Requests);
    }

    [Theory, AutoDomainData]
    public async Task Given_MessagesFromTwoOrganizations_When_GetByOrganizationAsync_Then_GroupsAndCountsPerOrganization(
        Guid databaseId)
    {
        // Given
        var organizationA = CreateOrganization("MetalPro");
        var organizationB = CreateOrganization("AtelierNordik");
        var userA1 = Guid.NewGuid();
        var userA2 = Guid.NewGuid();
        var userB1 = Guid.NewGuid();
        var conversationA1 = CreateConversation(organizationA.Id, userA1);
        var conversationA2 = CreateConversation(organizationA.Id, userA2);
        var conversationB1 = CreateConversation(organizationB.Id, userB1);
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.AddRange(organizationA, organizationB);
        dbContext.Conversations.AddRange(conversationA1, conversationA2, conversationB1);
        dbContext.Messages.AddRange(
            CreateMessage(conversationA1.Id, TodayStart.AddHours(9), MessageRole.User),
            CreateMessage(conversationA2.Id, TodayStart.AddHours(10), MessageRole.User),
            CreateMessage(conversationA2.Id, TodayStart.AddHours(11), MessageRole.User),
            CreateMessage(conversationB1.Id, TodayStart.AddHours(9), MessageRole.User));
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeUsageQueries(dbContext);

        // When
        var result = await queries.GetByOrganizationAsync(TodayStart, TodayStart.AddDays(1));

        // Then
        Assert.Equal(2, result.Count);
        var itemA = Assert.Single(result, item => item.OrganizationId == organizationA.Id);
        Assert.Equal("MetalPro", itemA.OrganizationName);
        Assert.Equal(2, itemA.ActiveUsers);
        Assert.Equal(3, itemA.Requests);
        var itemB = Assert.Single(result, item => item.OrganizationId == organizationB.Id);
        Assert.Equal(1, itemB.ActiveUsers);
        Assert.Equal(1, itemB.Requests);
    }

    private static AssistantCoreDbContext CreateDbContext(Guid databaseId)
    {
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(databaseId.ToString())
            .Options;

        return new AssistantCoreDbContext(options);
    }

    private static Organization CreateOrganization(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Domain = $"{name.ToLowerInvariant()}.test",
        IdentityProvider = IdentityProvider.MicrosoftEntraId,
        ExternalTenantId = null,
        Status = RecordStatus.Active,
        CreatedAt = DateTimeOffset.Parse("2026-06-15T00:00:00Z")
    };

    private static Conversation CreateConversation(Guid organizationId, Guid ownerMemberId) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        OwnerMemberId = ownerMemberId,
        Title = "Conversation de test",
        Status = ConversationStatus.Active,
        Version = 1,
        CreatedAt = DateTimeOffset.Parse("2026-06-15T00:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2026-06-15T00:00:00Z")
    };

    private static Message CreateMessage(
        Guid conversationId,
        DateTimeOffset createdAt,
        MessageRole role) => new()
    {
        Id = Guid.NewGuid(),
        ConversationId = conversationId,
        Role = role,
        Content = role == MessageRole.User ? "Question de test." : "Reponse de test.",
        ProcessingStatus = MessageProcessingStatus.Completed,
        CreatedAt = createdAt,
        UpdatedAt = createdAt
    };
}
