using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using AssistantCore.Repository.Queries;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Service.Tests.Repository;

public sealed class BackofficeMessageWarningQueriesTests
{
    [Theory, AutoDomainData]
    public async Task Given_AnOrganizationFilter_When_SearchAsync_Then_OnlyThatOrganizationsWarningsAreReturned(
        Guid databaseId)
    {
        // Given
        var organizationA = CreateOrganization("MetalPro");
        var organizationB = CreateOrganization("AtelierNordik");
        var conversationA = CreateConversation(organizationA.Id);
        var conversationB = CreateConversation(organizationB.Id);
        var messageA = CreateMessage(conversationA.Id);
        var messageB = CreateMessage(conversationB.Id);
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.AddRange(organizationA, organizationB);
        dbContext.Conversations.AddRange(conversationA, conversationB);
        dbContext.Messages.AddRange(messageA, messageB);
        dbContext.MessageWarnings.AddRange(
            CreateWarning(messageA.Id, "Reponse incertaine."),
            CreateWarning(messageB.Id, "Source manquante."));
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeMessageWarningQueries(dbContext);

        // When
        var result = await queries.SearchAsync(organizationA.Id, null, null, 1, 25);

        // Then
        var item = Assert.Single(result.Items);
        Assert.Equal(organizationA.Id, item.OrganizationId);
        Assert.Equal("Reponse incertaine.", item.Content);
    }

    [Theory, AutoDomainData]
    public async Task Given_TwoOrganizationsWarnings_When_SearchAsyncFilteredByOrganizationA_Then_OrganizationBWarningsAreNeverReturned(
        Guid databaseId)
    {
        // Given : garde d'isolation multi-tenant.
        var organizationA = CreateOrganization("MetalPro");
        var organizationB = CreateOrganization("AtelierNordik");
        var conversationA = CreateConversation(organizationA.Id);
        var conversationB = CreateConversation(organizationB.Id);
        var messageA = CreateMessage(conversationA.Id);
        var messageB = CreateMessage(conversationB.Id);
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.AddRange(organizationA, organizationB);
        dbContext.Conversations.AddRange(conversationA, conversationB);
        dbContext.Messages.AddRange(messageA, messageB);
        dbContext.MessageWarnings.AddRange(
            CreateWarning(messageA.Id, "Reponse incertaine."),
            CreateWarning(messageB.Id, "Source manquante."));
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeMessageWarningQueries(dbContext);

        // When
        var result = await queries.SearchAsync(organizationA.Id, null, null, 1, 25);

        // Then
        Assert.All(result.Items, item => Assert.Equal(organizationA.Id, item.OrganizationId));
    }

    [Theory, AutoDomainData]
    public async Task Given_APeriodFilter_When_SearchAsync_Then_OnlyWarningsWithinThePeriodAreReturned(
        Guid databaseId)
    {
        // Given
        var organization = CreateOrganization("MetalPro");
        var conversation = CreateConversation(organization.Id);
        var inRangeMessage = CreateMessage(conversation.Id, DateTimeOffset.Parse("2026-09-15T12:00:00Z"));
        var beforeRangeMessage = CreateMessage(conversation.Id, DateTimeOffset.Parse("2026-09-01T00:00:00Z"));
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.Add(organization);
        dbContext.Conversations.Add(conversation);
        dbContext.Messages.AddRange(inRangeMessage, beforeRangeMessage);
        dbContext.MessageWarnings.AddRange(
            CreateWarning(inRangeMessage.Id, "In range."),
            CreateWarning(beforeRangeMessage.Id, "Before range."));
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeMessageWarningQueries(dbContext);

        // When
        var result = await queries.SearchAsync(
            null,
            DateTimeOffset.Parse("2026-09-10T00:00:00Z"),
            DateTimeOffset.Parse("2026-09-20T00:00:00Z"),
            1,
            25);

        // Then
        var item = Assert.Single(result.Items);
        Assert.Equal("In range.", item.Content);
    }

    [Theory, AutoDomainData]
    public async Task Given_NoWarningMatches_When_SearchAsync_Then_ReturnsAnEmptyPage(Guid databaseId)
    {
        // Given
        await using var dbContext = CreateDbContext(databaseId);
        var queries = new BackofficeMessageWarningQueries(dbContext);

        // When
        var result = await queries.SearchAsync(Guid.NewGuid(), null, null, 1, 25);

        // Then
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
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

    private static Conversation CreateConversation(Guid organizationId) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        OwnerMemberId = Guid.NewGuid(),
        Title = "Conversation de test",
        Status = ConversationStatus.Active,
        Version = 1,
        CreatedAt = DateTimeOffset.Parse("2026-09-10T00:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2026-09-10T00:00:00Z")
    };

    private static Message CreateMessage(Guid conversationId, DateTimeOffset? createdAt = null)
    {
        var timestamp = createdAt ?? DateTimeOffset.Parse("2026-09-15T00:00:00Z");
        return new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = MessageRole.Assistant,
            Content = "Reponse de test.",
            ProcessingStatus = MessageProcessingStatus.Completed,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    private static MessageWarning CreateWarning(Guid messageId, string content) => new()
    {
        Id = Guid.NewGuid(),
        MessageId = messageId,
        Content = content
    };
}
