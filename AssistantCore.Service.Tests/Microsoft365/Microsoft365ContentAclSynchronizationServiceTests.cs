using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Models.Microsoft365.Permissions;
using AssistantCore.Service.Application.Services.Microsoft365;
using AssistantCore.Service.Application.Configuration;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365ContentAclSynchronizationServiceTests
{
    [Theory, AutoDomainData]
    public async Task Given_HiddenPassagesAlreadyUploaded_When_RegisterAsync_Then_DoesNotRewriteAvailability(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        string chunkId)
    {
        // Given
        var acl = CreateAcl("user-1");
        var content = CreateContent(
            organizationId,
            sourceId,
            externalContentId,
            chunkId,
            acl.Fingerprint,
            false);
        var repository = new RecordingRepository(content);
        var writer = new RecordingPassageWriter();
        var service = CreateService(repository, writer);

        // When
        await service.RegisterAsync(
            organizationId,
            sourceId,
            externalContentId,
            [chunkId],
            acl.Fingerprint,
            "https://contoso.sharepoint.com");

        // Then
        Assert.Empty(writer.Operations);
        Assert.False(content.IsAvailable);
        Assert.Equal(1, repository.SaveCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnUnchangedPublishedAcl_When_SynchronizeAsync_Then_DoesNotRewritePassages(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        string chunkId)
    {
        // Given
        var acl = CreateAcl("user-1");
        var content = CreateContent(organizationId, sourceId, externalContentId, chunkId, acl.Fingerprint, true);
        var repository = new RecordingRepository(content);
        var writer = new RecordingPassageWriter();
        var service = CreateService(repository, writer);

        // When
        var result = await service.SynchronizeAsync(organizationId, sourceId, externalContentId, acl);

        // Then
        Assert.Equal(Microsoft365AclSynchronizationResult.Unchanged, result);
        Assert.Empty(writer.Operations);
        Assert.Equal(1, repository.SaveCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_AChangedAcl_When_SynchronizeAsync_Then_HidesUpdatesAndPublishesEveryPassage(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        string firstChunkId,
        string secondChunkId)
    {
        // Given
        var acl = CreateAcl("new-user");
        var content = CreateContent(
            organizationId,
            sourceId,
            externalContentId,
            firstChunkId,
            "OLD-FINGERPRINT",
            true,
            secondChunkId);
        var repository = new RecordingRepository(content);
        var writer = new RecordingPassageWriter();
        var service = CreateService(repository, writer);

        // When
        var result = await service.SynchronizeAsync(organizationId, sourceId, externalContentId, acl);

        // Then
        Assert.Equal(Microsoft365AclSynchronizationResult.Updated, result);
        Assert.Equal(["availability:False", "acl", "availability:True"], writer.Operations);
        Assert.All(writer.ReceivedChunkSets, chunks => Assert.Equal(2, chunks.Count));
        Assert.Equal(acl.Fingerprint, content.AclFingerprint);
        Assert.True(content.IsAvailable);
        Assert.Equal(3, repository.SaveCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnAclWriteFailure_When_SynchronizeAsync_Then_ContentRemainsUnavailable(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        string chunkId)
    {
        // Given
        var content = CreateContent(
            organizationId,
            sourceId,
            externalContentId,
            chunkId,
            "OLD-FINGERPRINT",
            true);
        var repository = new RecordingRepository(content);
        var writer = new RecordingPassageWriter { FailAclUpdate = true };
        var service = CreateService(repository, writer);

        // When
        var action = () => service.SynchronizeAsync(
            organizationId,
            sourceId,
            externalContentId,
            CreateAcl("new-user"));

        // Then
        await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.False(content.IsAvailable);
        Assert.Equal("OLD-FINGERPRINT", content.AclFingerprint);
        Assert.Equal(["availability:False", "acl"], writer.Operations);
        Assert.Equal(1, repository.SaveCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnAppliedAclNotYetPublished_When_SynchronizeAsync_Then_PublishesWithoutRewritingAcl(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        string chunkId)
    {
        // Given
        var acl = CreateAcl("user-1");
        var content = CreateContent(organizationId, sourceId, externalContentId, chunkId, acl.Fingerprint, false);
        var repository = new RecordingRepository(content);
        var writer = new RecordingPassageWriter();
        var service = CreateService(repository, writer);

        // When
        var result = await service.SynchronizeAsync(organizationId, sourceId, externalContentId, acl);

        // Then
        Assert.Equal(Microsoft365AclSynchronizationResult.Published, result);
        Assert.Equal(["availability:True"], writer.Operations);
        Assert.True(content.IsAvailable);
        Assert.Equal(1, repository.SaveCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_AResolutionFailure_When_MarkUnavailableIfRegisteredAsync_Then_HidesEveryPassageAndSchedulesRetry(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        string chunkId)
    {
        // Given
        var content = CreateContent(
            organizationId,
            sourceId,
            externalContentId,
            chunkId,
            "CURRENT-FINGERPRINT",
            true);
        var repository = new RecordingRepository(content);
        var writer = new RecordingPassageWriter();
        var service = CreateService(repository, writer);

        // When
        var wasRegistered = await service.MarkUnavailableIfRegisteredAsync(
            organizationId,
            sourceId,
            externalContentId);

        // Then
        Assert.True(wasRegistered);
        Assert.False(content.IsAvailable);
        Assert.NotNull(content.NextAclReconciliationAt);
        Assert.Equal(["availability:False"], writer.Operations);
        Assert.Equal(1, repository.SaveCount);
    }

    private static Microsoft365ContentAclSynchronizationService CreateService(
        IMicrosoft365IndexedContentRepository repository,
        IMicrosoft365PassageAclWriter writer) =>
        new(
            repository,
            writer,
            Options.Create(new Microsoft365Options()),
            TimeProvider.System);

    private static Microsoft365Acl CreateAcl(string userId) =>
        new([userId], [], [], false, false, Microsoft365AclInheritance.Unique);

    private static Microsoft365IndexedContent CreateContent(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        string firstChunkId,
        string? fingerprint,
        bool isAvailable,
        string? secondChunkId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Microsoft365SourceId = sourceId,
            ExternalContentId = externalContentId,
            AclFingerprint = fingerprint,
            IsAvailable = isAvailable,
            Passages = new[] { firstChunkId, secondChunkId }
                .Where(chunkId => chunkId is not null)
                .Select(chunkId => new Microsoft365IndexedPassage
                {
                    Id = Guid.NewGuid(),
                    ChunkId = chunkId!
                })
                .ToArray()
        };

    private sealed class RecordingRepository(Microsoft365IndexedContent content)
        : IMicrosoft365IndexedContentRepository
    {
        public int SaveCount { get; private set; }

        public Task<Microsoft365IndexedContent?> FindAsync(
            Guid organizationId,
            Guid sourceId,
            string externalContentId,
            CancellationToken cancellationToken = default) => Task.FromResult<Microsoft365IndexedContent?>(content);

        public Task<IReadOnlyCollection<Microsoft365IndexedContent>> GetAclReconciliationCandidatesAsync(
            DateTimeOffset dueAt,
            int maximumResults,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<Microsoft365IndexedContent>>([content]);

        public Task RequestAclReconciliationAsync(
            Guid sourceId,
            DateTimeOffset dueAt,
            CancellationToken cancellationToken = default)
        {
            content.NextAclReconciliationAt = dueAt;
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            Microsoft365IndexedContent indexedContent,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPassageWriter : IMicrosoft365PassageAclWriter
    {
        public List<string> Operations { get; } = [];
        public List<IReadOnlyCollection<string>> ReceivedChunkSets { get; } = [];
        public bool FailAclUpdate { get; init; }

        public Task SetAvailabilityAsync(
            IReadOnlyCollection<string> chunkIds,
            bool isAvailable,
            CancellationToken cancellationToken = default)
        {
            Operations.Add($"availability:{isAvailable}");
            ReceivedChunkSets.Add(chunkIds);
            return Task.CompletedTask;
        }

        public Task UpdateAclAsync(
            IReadOnlyCollection<string> chunkIds,
            Microsoft365Acl acl,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("acl");
            ReceivedChunkSets.Add(chunkIds);
            return FailAclUpdate
                ? Task.FromException(new InvalidOperationException("ACL update failed."))
                : Task.CompletedTask;
        }
    }
}
