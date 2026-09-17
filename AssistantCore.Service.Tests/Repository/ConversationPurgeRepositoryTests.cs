using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using AssistantCore.Repository.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory.Internal;

namespace AssistantCore.Service.Tests.Repository;

public sealed class ConversationPurgeRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [Theory, AutoDomainData]
    public async Task Given_ADueRequest_When_ClaimNextAsync_Then_TakesTheLeaseAndCountsTheAttempt(
        Guid databaseId)
    {
        // Given
        await using var dbContext = CreateDbContext(databaseId);
        var seeded = await SeedAsync(dbContext, "Alpha", purgeAfter: Now.AddMinutes(-1));
        var repository = new ConversationPurgeRepository(dbContext);
        var leaseId = Guid.NewGuid();

        // When
        var claimed = await repository.ClaimNextAsync(leaseId, Now, Now.AddMinutes(10));

        // Then
        Assert.NotNull(claimed);
        Assert.Equal(seeded.PurgeRequestId, claimed.Id);
        Assert.Equal(ConversationPurgeStatus.Processing, claimed.Status);
        Assert.Equal(leaseId, claimed.LeaseId);
        Assert.Equal(1, claimed.AttemptCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_ARequestNotYetDue_When_ClaimNextAsync_Then_ReturnsNothing(
        Guid databaseId)
    {
        // Given
        // La periode de recuperation protege la conversation : tant qu'elle court,
        // la purge ne doit pas commencer.
        await using var dbContext = CreateDbContext(databaseId);
        await SeedAsync(dbContext, "Alpha", purgeAfter: Now.AddDays(5));
        var repository = new ConversationPurgeRepository(dbContext);

        // When
        var claimed = await repository.ClaimNextAsync(Guid.NewGuid(), Now, Now.AddMinutes(10));

        // Then
        Assert.Null(claimed);
    }

    [Theory, AutoDomainData]
    public async Task Given_ARequestHeldByALiveLease_When_ClaimNextAsync_Then_ReturnsNothing(
        Guid databaseId)
    {
        // Given
        await using var dbContext = CreateDbContext(databaseId);
        await SeedAsync(dbContext, "Alpha", purgeAfter: Now.AddMinutes(-1));
        var repository = new ConversationPurgeRepository(dbContext);
        await repository.ClaimNextAsync(Guid.NewGuid(), Now, Now.AddMinutes(10));

        // When
        var second = await repository.ClaimNextAsync(Guid.NewGuid(), Now, Now.AddMinutes(10));

        // Then
        Assert.Null(second);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnExpiredLease_When_ClaimNextAsync_Then_TheRequestIsClaimableAgain(
        Guid databaseId)
    {
        // Given
        // Un worker disparu en cours de purge ne doit jamais bloquer la demande.
        await using var dbContext = CreateDbContext(databaseId);
        await SeedAsync(dbContext, "Alpha", purgeAfter: Now.AddMinutes(-1));
        var repository = new ConversationPurgeRepository(dbContext);
        await repository.ClaimNextAsync(Guid.NewGuid(), Now, Now.AddMinutes(10));

        // When
        var reclaimed = await repository.ClaimNextAsync(
            Guid.NewGuid(),
            Now.AddMinutes(20),
            Now.AddMinutes(30));

        // Then
        Assert.NotNull(reclaimed);
        Assert.Equal(2, reclaimed.AttemptCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_AClaimedRequest_When_RunningEveryStep_Then_TheConversationDisappears(
        Guid databaseId)
    {
        // Given
        await using var dbContext = CreateDbContext(databaseId);
        var seeded = await SeedAsync(dbContext, "Alpha", purgeAfter: Now.AddMinutes(-1));
        var repository = new ConversationPurgeRepository(dbContext);
        var leaseId = Guid.NewGuid();
        var claimed = await repository.ClaimNextAsync(leaseId, Now, Now.AddMinutes(10));
        Assert.NotNull(claimed);

        // When
        var sources = await repository.DeleteMessageSourcesAsync(claimed.Id, leaseId);
        var messages = await repository.DeleteMessagesAsync(claimed.Id, leaseId);
        await repository.DeleteConversationAsync(claimed.Id, leaseId);

        // Then
        Assert.Equal(1, sources);
        Assert.Equal(2, messages);
        Assert.True(await repository.VerifyPurgedAsync(claimed.Id));
        Assert.False(await dbContext.Conversations
            .AnyAsync(candidate => candidate.Id == seeded.ConversationId));
        // La demande survit a la conversation : elle porte la preuve de la purge.
        Assert.True(await dbContext.ConversationPurgeRequests
            .AnyAsync(candidate => candidate.Id == seeded.PurgeRequestId));
    }

    [Theory, AutoDomainData]
    public async Task Given_StepsAlreadyRun_When_RunningThemAgain_Then_TheyAreIdempotent(
        Guid databaseId)
    {
        // Given
        await using var dbContext = CreateDbContext(databaseId);
        await SeedAsync(dbContext, "Alpha", purgeAfter: Now.AddMinutes(-1));
        var repository = new ConversationPurgeRepository(dbContext);
        var leaseId = Guid.NewGuid();
        var claimed = await repository.ClaimNextAsync(leaseId, Now, Now.AddMinutes(10));
        Assert.NotNull(claimed);
        await repository.DeleteMessageSourcesAsync(claimed.Id, leaseId);
        await repository.DeleteMessagesAsync(claimed.Id, leaseId);
        await repository.DeleteConversationAsync(claimed.Id, leaseId);

        // When
        var sources = await repository.DeleteMessageSourcesAsync(claimed.Id, leaseId);
        var messages = await repository.DeleteMessagesAsync(claimed.Id, leaseId);
        await repository.DeleteConversationAsync(claimed.Id, leaseId);

        // Then
        Assert.Equal(0, sources);
        Assert.Equal(0, messages);
        Assert.True(await repository.VerifyPurgedAsync(claimed.Id));
    }

    [Theory, AutoDomainData]
    public async Task Given_AStaleLease_When_Writing_Then_NothingIsChanged(
        Guid databaseId)
    {
        // Given
        // Un worker dont le bail a ete repris ailleurs ne doit plus rien modifier.
        await using var dbContext = CreateDbContext(databaseId);
        await SeedAsync(dbContext, "Alpha", purgeAfter: Now.AddMinutes(-1));
        var repository = new ConversationPurgeRepository(dbContext);
        var claimed = await repository.ClaimNextAsync(Guid.NewGuid(), Now, Now.AddMinutes(10));
        Assert.NotNull(claimed);

        // When
        var deleted = await repository.DeleteMessagesAsync(claimed.Id, Guid.NewGuid());

        // Then
        Assert.Equal(0, deleted);
        Assert.Equal(2, await dbContext.Messages.CountAsync());
    }

    [Theory, AutoDomainData]
    public async Task Given_TwoOrganizations_When_PurgingOne_Then_TheOtherIsUntouched(
        Guid databaseId)
    {
        // Given
        await using var dbContext = CreateDbContext(databaseId);
        var alpha = await SeedAsync(dbContext, "Alpha", purgeAfter: Now.AddMinutes(-1));
        var beta = await SeedAsync(dbContext, "Beta", purgeAfter: Now.AddDays(5));
        var repository = new ConversationPurgeRepository(dbContext);
        var leaseId = Guid.NewGuid();
        var claimed = await repository.ClaimNextAsync(leaseId, Now, Now.AddMinutes(10));
        Assert.NotNull(claimed);
        Assert.Equal(alpha.PurgeRequestId, claimed.Id);

        // When
        await repository.DeleteMessageSourcesAsync(claimed.Id, leaseId);
        await repository.DeleteMessagesAsync(claimed.Id, leaseId);
        await repository.DeleteConversationAsync(claimed.Id, leaseId);

        // Then
        Assert.True(await dbContext.Conversations
            .AnyAsync(candidate => candidate.Id == beta.ConversationId));
        Assert.Equal(2, await dbContext.Messages.CountAsync());
        Assert.Equal(1, await dbContext.MessageSources.CountAsync());
    }

    [Theory, AutoDomainData]
    public async Task Given_ARemainingMessage_When_VerifyPurgedAsync_Then_ItRefusesToConfirm(
        Guid databaseId)
    {
        // Given
        // La verification est ce qui autorise Completed : elle doit refuser tant
        // qu'une ligne subsiste.
        await using var dbContext = CreateDbContext(databaseId);
        await SeedAsync(dbContext, "Alpha", purgeAfter: Now.AddMinutes(-1));
        var repository = new ConversationPurgeRepository(dbContext);
        var leaseId = Guid.NewGuid();
        var claimed = await repository.ClaimNextAsync(leaseId, Now, Now.AddMinutes(10));
        Assert.NotNull(claimed);
        await repository.DeleteMessageSourcesAsync(claimed.Id, leaseId);

        // When
        var verified = await repository.VerifyPurgedAsync(claimed.Id);

        // Then
        Assert.False(verified);
    }

    [Theory, AutoDomainData]
    public async Task Given_APermanentFailure_When_ClaimNextAsync_Then_ItIsNotRetried(
        Guid databaseId)
    {
        // Given
        await using var dbContext = CreateDbContext(databaseId);
        await SeedAsync(dbContext, "Alpha", purgeAfter: Now.AddMinutes(-1));
        var repository = new ConversationPurgeRepository(dbContext);
        var leaseId = Guid.NewGuid();
        var claimed = await repository.ClaimNextAsync(leaseId, Now, Now.AddMinutes(10));
        Assert.NotNull(claimed);
        await repository.FailAsync(claimed.Id, leaseId, "SqlException", isPermanent: true, null);

        // When
        var reclaimed = await repository.ClaimNextAsync(
            Guid.NewGuid(),
            Now.AddDays(1),
            Now.AddDays(1).AddMinutes(10));

        // Then
        Assert.Null(reclaimed);
    }

    [Theory, AutoDomainData]
    public async Task Given_ATemporaryFailure_When_TheBackoffHasElapsed_Then_ItIsRetried(
        Guid databaseId)
    {
        // Given
        await using var dbContext = CreateDbContext(databaseId);
        await SeedAsync(dbContext, "Alpha", purgeAfter: Now.AddMinutes(-1));
        var repository = new ConversationPurgeRepository(dbContext);
        var leaseId = Guid.NewGuid();
        var claimed = await repository.ClaimNextAsync(leaseId, Now, Now.AddMinutes(10));
        Assert.NotNull(claimed);
        await repository.FailAsync(
            claimed.Id,
            leaseId,
            "SqlException",
            isPermanent: false,
            Now.AddMinutes(5));

        // When
        var tooEarly = await repository.ClaimNextAsync(
            Guid.NewGuid(),
            Now.AddMinutes(1),
            Now.AddMinutes(11));
        var later = await repository.ClaimNextAsync(
            Guid.NewGuid(),
            Now.AddMinutes(6),
            Now.AddMinutes(16));

        // Then
        Assert.Null(tooEarly);
        Assert.NotNull(later);
    }

    private sealed record SeededConversation(Guid ConversationId, Guid PurgeRequestId);

    private static async Task<SeededConversation> SeedAsync(
        AssistantCoreDbContext dbContext,
        string name,
        DateTimeOffset purgeAfter)
    {
        var organizationId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var purgeRequestId = Guid.NewGuid();
        var firstMessageId = Guid.NewGuid();

        var organization = new Organization { Id = organizationId, Name = name };
        var member = new OrganizationMember
        {
            Id = memberId,
            OrganizationId = organizationId,
            Name = $"Membre {name}",
            Email = $"membre@{name}.test",
            IdentityProvider = IdentityProvider.MicrosoftEntraId,
            ExternalUserId = $"user-{name}",
            Role = OrganizationRole.User,
            Status = RecordStatus.Active
        };
        var conversation = new Conversation
        {
            Id = conversationId,
            OrganizationId = organizationId,
            OwnerMemberId = memberId,
            Title = $"Conversation {name}",
            Status = ConversationStatus.Active,
            Version = 1,
            CreatedAt = Now.AddDays(-40),
            UpdatedAt = Now.AddDays(-40),
            DeletedAt = Now.AddDays(-31)
        };
        var firstMessage = new Message
        {
            Id = firstMessageId,
            ConversationId = conversationId,
            Role = MessageRole.User,
            Content = $"Question {name}",
            ProcessingStatus = MessageProcessingStatus.Completed,
            CreatedAt = Now.AddDays(-40)
        };
        var secondMessage = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = MessageRole.Assistant,
            Content = $"Reponse {name}",
            ProcessingStatus = MessageProcessingStatus.Completed,
            CreatedAt = Now.AddDays(-40)
        };
        var source = new MessageSource
        {
            Id = Guid.NewGuid(),
            MessageId = secondMessage.Id,
            SourceType = "sharepoint",
            Title = $"Document {name}",
            Reference = $"reference-{name}"
        };
        var purgeRequest = new ConversationPurgeRequest
        {
            Id = purgeRequestId,
            ConversationId = conversationId,
            OrganizationId = organizationId,
            RequestedAt = Now.AddDays(-31),
            PurgeAfter = purgeAfter,
            Status = ConversationPurgeStatus.Pending,
            Step = ConversationPurgeStep.DeleteMessageSources
        };

        dbContext.AddRange(
            organization,
            member,
            conversation,
            firstMessage,
            secondMessage,
            source,
            purgeRequest);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        return new SeededConversation(conversationId, purgeRequestId);
    }

    /// <summary>
    /// Le fournisseur en memoire ignore les transactions. Ces tests verifient donc
    /// la logique de reclamation, de bail et de reprise, mais pas l'isolation
    /// Serializable elle-meme : seule une vraie base SQL Server peut prouver que
    /// deux workers concurrents ne reclament jamais la meme demande.
    /// </summary>
    private static AssistantCoreDbContext CreateDbContext(Guid databaseId)
    {
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(databaseId.ToString())
            .ConfigureWarnings(builder => builder.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AssistantCoreDbContext(options);
    }
}
