using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Models.Microsoft365.Permissions;
using AssistantCore.Service.Application.Services.Microsoft365;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365IndexCleanupServiceTests
{
    [Theory, AutoDomainData]
    public async Task Given_IndexedContent_When_CleanupAsync_Then_DeletesItsUniqueChunksAndCompletesTheSynchronization(
        Guid organizationId,
        Guid sourceId,
        Guid synchronizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Given
        var firstContent = CreateContent(organizationId, sourceId, "chunk-1", "chunk-2");
        var secondContent = CreateContent(organizationId, sourceId, "chunk-2", "chunk-3");
        var contentRepository = new StubIndexedContentRepository([firstContent, secondContent]);
        var indexWriter = new StubPassageIndexWriter();
        var synchronizationRepository = new StubSourceSynchronizationRepository();
        var service = new Microsoft365IndexCleanupService(
            contentRepository,
            indexWriter,
            synchronizationRepository,
            new FixedTimeProvider(now));

        // When
        await service.CleanupAsync(
            organizationId,
            sourceId,
            synchronizationId,
            cancellationToken);

        // Then
        Assert.Equal(["chunk-1", "chunk-2", "chunk-3"], indexWriter.DeletedChunkIds);
        Assert.Equal([firstContent, secondContent], contentRepository.DeletedContents);
        Assert.Equal(sourceId, synchronizationRepository.SourceId);
        Assert.Equal(synchronizationId, synchronizationRepository.SynchronizationId);
        Assert.Equal(Microsoft365SynchronizationStatus.Succeeded, synchronizationRepository.Status);
        Assert.Equal(now, synchronizationRepository.CompletedAt);
    }

    private static Microsoft365IndexedContent CreateContent(
        Guid organizationId,
        Guid sourceId,
        params string[] chunkIds) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Microsoft365SourceId = sourceId,
            Passages = chunkIds.Select(chunkId => new Microsoft365IndexedPassage
            {
                Id = Guid.NewGuid(),
                ChunkId = chunkId
            }).ToList()
        };

    private sealed class StubIndexedContentRepository(
        IReadOnlyCollection<Microsoft365IndexedContent> contents)
        : IMicrosoft365IndexedContentRepository
    {
        public List<Microsoft365IndexedContent> DeletedContents { get; } = [];

        public Task<Microsoft365IndexedContent?> FindAsync(Guid organizationId, Guid sourceId, string externalContentId, CancellationToken cancellationToken = default) => Task.FromResult<Microsoft365IndexedContent?>(null);

        public Task<IReadOnlyCollection<Microsoft365IndexedContent>> GetAclReconciliationCandidatesAsync(DateTimeOffset dueAt, int maximumResults, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<Microsoft365IndexedContent>>([]);

        public Task<IReadOnlyCollection<Microsoft365IndexedContent>> GetBySourceAsync(Guid organizationId, Guid sourceId, CancellationToken cancellationToken = default) => Task.FromResult(contents);

        public Task RequestAclReconciliationAsync(Guid sourceId, DateTimeOffset dueAt, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(Microsoft365IndexedContent content, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(Microsoft365IndexedContent content, CancellationToken cancellationToken = default)
        {
            DeletedContents.Add(content);
            return Task.CompletedTask;
        }
    }

    private sealed class StubPassageIndexWriter : IMicrosoft365PassageIndexWriter
    {
        public IReadOnlyCollection<string> DeletedChunkIds { get; private set; } = [];

        public Task MergeOrUploadAsync(Guid organizationId, IReadOnlyCollection<Microsoft365SearchPassage> passages, Microsoft365Acl acl, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(Guid organizationId, IReadOnlyCollection<string> chunkIds, CancellationToken cancellationToken = default)
        {
            DeletedChunkIds = chunkIds;
            return Task.CompletedTask;
        }
    }

    private sealed class StubSourceSynchronizationRepository : IMicrosoft365SourceSynchronizationRepository
    {
        public Guid? SourceId { get; private set; }
        public Guid? SynchronizationId { get; private set; }
        public Microsoft365SynchronizationStatus? Status { get; private set; }
        public DateTimeOffset? CompletedAt { get; private set; }

        public Task<bool> RecordSynchronizationOutcomeAsync(Guid sourceId, Guid synchronizationId, Microsoft365SynchronizationStatus status, Microsoft365SynchronizationCounters counters, DateTimeOffset completedAt, string? lastErrorCode, CancellationToken cancellationToken = default)
        {
            SourceId = sourceId;
            SynchronizationId = synchronizationId;
            Status = status;
            CompletedAt = completedAt;
            return Task.FromResult(true);
        }

        public Task<bool> TryAcquireLeaseAsync(Guid sourceId, Guid leaseId, DateTimeOffset attemptedAt, DateTimeOffset expiresAt, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> ConfirmCheckpointAsync(Guid sourceId, Guid leaseId, string deltaLink, DateTimeOffset completedAt, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> MarkFullResyncRequiredAsync(Guid sourceId, Guid leaseId, string lastErrorCode, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> MarkAccessErrorAsync(Guid sourceId, Guid leaseId, string lastErrorCode, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ReleaseLeaseAsync(Guid sourceId, Guid leaseId, string? lastErrorCode, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
