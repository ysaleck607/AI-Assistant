using AssistantCore.Repository.Domain;
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
        var result = await queries.SearchAsync(organizationA.Id, null, null, false, 1, 25);

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
        var result = await queries.SearchAsync(organizationA.Id, null, null, false, 1, 25);

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
            false,
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
        var result = await queries.SearchAsync(Guid.NewGuid(), null, null, false, 1, 25);

        // Then
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_ContentGapAndRegularWarning_When_SearchAsyncWithContentGapsOnly_Then_OnlyTheContentGapIsReturned(
        Guid databaseId)
    {
        // Given
        var organization = CreateOrganization("MetalPro");
        var conversation = CreateConversation(organization.Id);
        var regularMessage = CreateMessage(conversation.Id);
        var gapMessage = CreateMessage(conversation.Id);
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.Add(organization);
        dbContext.Conversations.Add(conversation);
        dbContext.Messages.AddRange(regularMessage, gapMessage);
        dbContext.MessageWarnings.AddRange(
            CreateWarning(regularMessage.Id, "Reponse incertaine."),
            CreateWarning(
                gapMessage.Id,
                $"{MessageWarningMarkers.NoEvidenceFoundPrefix} Aucune preuve documentaire trouvee pour repondre a cette question."));
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeMessageWarningQueries(dbContext);

        // When
        var result = await queries.SearchAsync(organization.Id, null, null, true, 1, 25);

        // Then
        var item = Assert.Single(result.Items);
        Assert.True(item.IsContentGap);
        Assert.Equal(gapMessage.Id, item.MessageId);
        Assert.Equal(
            "Aucune preuve documentaire trouvee pour repondre a cette question.",
            item.Content);
    }

    [Theory, AutoDomainData]
    public async Task Given_ContentGapWarning_When_SearchAsync_Then_QuestionTextIsThePrecedingUserMessage(
        Guid databaseId)
    {
        // Given
        var organization = CreateOrganization("MetalPro");
        var conversation = CreateConversation(organization.Id);
        var userMessage = CreateMessage(
            conversation.Id,
            DateTimeOffset.Parse("2026-09-15T10:00:00Z"),
            MessageRole.User,
            "Quel est le processus de remboursement ?");
        var assistantMessage = CreateMessage(
            conversation.Id,
            DateTimeOffset.Parse("2026-09-15T10:00:05Z"));
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.Add(organization);
        dbContext.Conversations.Add(conversation);
        dbContext.Messages.AddRange(userMessage, assistantMessage);
        dbContext.MessageWarnings.Add(
            CreateWarning(
                assistantMessage.Id,
                $"{MessageWarningMarkers.NoEvidenceFoundPrefix} Aucune preuve documentaire trouvee pour repondre a cette question."));
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeMessageWarningQueries(dbContext);

        // When
        var result = await queries.SearchAsync(organization.Id, null, null, true, 1, 25);

        // Then
        var item = Assert.Single(result.Items);
        Assert.Equal("Quel est le processus de remboursement ?", item.QuestionText);
    }

    [Theory, AutoDomainData]
    public async Task Given_ContentGapWarning_When_SearchAsync_Then_ResponseTextIsTheModelsAnswer(
        Guid databaseId)
    {
        // Given : requis par Giovani - l'alerte de lacune de contenu doit exposer la
        // reponse du modele en plus de la question posee.
        var organization = CreateOrganization("MetalPro");
        var conversation = CreateConversation(organization.Id);
        var userMessage = CreateMessage(
            conversation.Id,
            DateTimeOffset.Parse("2026-09-15T10:00:00Z"),
            MessageRole.User,
            "Quel est le processus de remboursement ?");
        var assistantMessage = CreateMessage(
            conversation.Id,
            DateTimeOffset.Parse("2026-09-15T10:00:05Z"),
            content: "Je n'ai pas trouve d'information a ce sujet.");
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.Add(organization);
        dbContext.Conversations.Add(conversation);
        dbContext.Messages.AddRange(userMessage, assistantMessage);
        dbContext.MessageWarnings.Add(
            CreateWarning(
                assistantMessage.Id,
                $"{MessageWarningMarkers.NoEvidenceFoundPrefix} Aucune preuve documentaire trouvee pour repondre a cette question."));
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeMessageWarningQueries(dbContext);

        // When
        var result = await queries.SearchAsync(organization.Id, null, null, true, 1, 25);

        // Then
        var item = Assert.Single(result.Items);
        Assert.Equal("Je n'ai pas trouve d'information a ce sujet.", item.ResponseText);
    }

    [Theory, AutoDomainData]
    public async Task Given_RegularWarning_When_SearchAsync_Then_QuestionTextIsNeverPopulated(
        Guid databaseId)
    {
        // Given : garde de confidentialite - jamais le contenu du message pour les
        // avertissements qui ne sont pas des lacunes de contenu.
        var organization = CreateOrganization("MetalPro");
        var conversation = CreateConversation(organization.Id);
        var userMessage = CreateMessage(
            conversation.Id,
            DateTimeOffset.Parse("2026-09-15T10:00:00Z"),
            MessageRole.User,
            "Question confidentielle.");
        var assistantMessage = CreateMessage(
            conversation.Id,
            DateTimeOffset.Parse("2026-09-15T10:00:05Z"));
        await using var dbContext = CreateDbContext(databaseId);
        dbContext.Organizations.Add(organization);
        dbContext.Conversations.Add(conversation);
        dbContext.Messages.AddRange(userMessage, assistantMessage);
        dbContext.MessageWarnings.Add(CreateWarning(assistantMessage.Id, "Reponse incertaine."));
        await dbContext.SaveChangesAsync();
        var queries = new BackofficeMessageWarningQueries(dbContext);

        // When
        var result = await queries.SearchAsync(organization.Id, null, null, false, 1, 25);

        // Then
        var item = Assert.Single(result.Items);
        Assert.False(item.IsContentGap);
        Assert.Null(item.QuestionText);
        Assert.Null(item.ResponseText);
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

    private static Message CreateMessage(
        Guid conversationId,
        DateTimeOffset? createdAt = null,
        MessageRole role = MessageRole.Assistant,
        string content = "Reponse de test.")
    {
        var timestamp = createdAt ?? DateTimeOffset.Parse("2026-09-15T00:00:00Z");
        return new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = role,
            Content = content,
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
