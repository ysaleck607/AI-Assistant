using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Persistence;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AssistantCore.Service.Tests.Repository;

// EF Core's InMemory provider ties a named database to whichever model first initializes it, so
// these tests each use a single IFieldEncryptorFactory per database name rather than mixing
// encryptors against the same store — the real encrypt/decrypt round trip is already covered at
// the unit level by DataProtectionFieldEncryptorTests and EncryptedStringConverterTests.
public sealed class ConversationRepositoryEncryptionTests
{
    [Theory, AutoDomainData]
    public async Task Given_ARealEncryptor_When_ConversationAndMessageAreSavedAndReloaded_Then_ContentRoundTripsCorrectly(
        Guid organizationId,
        Guid ownerMemberId,
        Conversation conversation,
        Message userMessage)
    {
        // Given
        conversation.OrganizationId = organizationId;
        conversation.OwnerMemberId = ownerMemberId;
        conversation.Title = "Plaintext conversation title";
        userMessage.ConversationId = conversation.Id;
        userMessage.Content = "Plaintext message content";
        await using var dbContext = CreateDbContext(Guid.NewGuid().ToString(), CreateRealEncryptorFactory());
        var repository = new ConversationRepository(dbContext, new StubAdministrativeAuditRepository());

        // When
        await repository.CreateConversationWithFirstMessageAsync(
            organizationId,
            ownerMemberId,
            conversation,
            userMessage,
            CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        // Then
        var reloadedConversation = await dbContext.Conversations.SingleAsync();
        var reloadedMessage = await dbContext.Messages.SingleAsync();
        Assert.Equal("Plaintext conversation title", reloadedConversation.Title);
        Assert.Equal("Plaintext message content", reloadedMessage.Content);
    }

    [Theory, AutoDomainData]
    public async Task Given_MoreConversationsThanOneBatch_When_ReencryptAllConversationTitlesAsync_Then_ProcessesEveryRowAcrossBatches(
        Guid organizationId,
        Guid ownerMemberId,
        Conversation firstConversation,
        Message firstMessage,
        Conversation secondConversation,
        Message secondMessage,
        Conversation thirdConversation,
        Message thirdMessage)
    {
        // Given
        var encryptorFactory = CreateRealEncryptorFactory();
        await using var dbContext = CreateDbContext(Guid.NewGuid().ToString(), encryptorFactory);
        var repository = new ConversationRepository(dbContext, new StubAdministrativeAuditRepository());

        foreach (var (conversation, message) in new[]
                 {
                     (firstConversation, firstMessage),
                     (secondConversation, secondMessage),
                     (thirdConversation, thirdMessage)
                 })
        {
            conversation.OrganizationId = organizationId;
            conversation.OwnerMemberId = ownerMemberId;
            message.ConversationId = conversation.Id;
            await repository.CreateConversationWithFirstMessageAsync(
                organizationId,
                ownerMemberId,
                conversation,
                message,
                CancellationToken.None);
        }

        dbContext.ChangeTracker.Clear();

        // When
        var processed = await repository.ReencryptAllConversationTitlesAsync(2, CancellationToken.None);

        // Then
        Assert.Equal(3, processed);
        var titles = await dbContext.Conversations
            .OrderBy(c => c.Id)
            .Select(c => c.Title)
            .ToListAsync();
        Assert.Equal(
            new[] { firstConversation, secondConversation, thirdConversation }
                .OrderBy(c => c.Id)
                .Select(c => c.Title),
            titles);
    }

    [Theory, AutoDomainData]
    public async Task Given_NoConversations_When_ReencryptAllConversationTitlesAsync_Then_ReturnsZeroWithoutError(
        Guid organizationId)
    {
        // Given
        _ = organizationId;
        await using var dbContext = CreateDbContext(Guid.NewGuid().ToString(), CreateRealEncryptorFactory());
        var repository = new ConversationRepository(dbContext, new StubAdministrativeAuditRepository());

        // When
        var processed = await repository.ReencryptAllConversationTitlesAsync(50, CancellationToken.None);

        // Then
        Assert.Equal(0, processed);
    }

    private static AssistantCoreDbContext CreateDbContext(
        string databaseName,
        IFieldEncryptorFactory fieldEncryptorFactory)
    {
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new AssistantCoreDbContext(options, fieldEncryptorFactory);
    }

    private static IFieldEncryptorFactory CreateRealEncryptorFactory()
    {
        var services = new ServiceCollection();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        var provider = services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
        return new DataProtectionFieldEncryptorFactory(provider);
    }
}
