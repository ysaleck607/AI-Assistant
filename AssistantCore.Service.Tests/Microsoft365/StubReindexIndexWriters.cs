using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Models.Microsoft365.Permissions;
using AssistantCore.Service.Application.Services.Microsoft365;

namespace AssistantCore.Service.Tests.Microsoft365;

internal sealed class StubReindexIndexedContentRepository(
    IReadOnlyCollection<Microsoft365IndexedContent> contents)
    : IMicrosoft365IndexedContentRepository
{
    public List<Microsoft365IndexedContent> DeletedContents { get; } = [];

    public Task<Microsoft365IndexedContent?> FindAsync(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<Microsoft365IndexedContent?>(null);

    public Task<IReadOnlyCollection<Microsoft365IndexedContent>> GetAclReconciliationCandidatesAsync(
        DateTimeOffset dueAt,
        int maximumResults,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<Microsoft365IndexedContent>>([]);

    public Task<IReadOnlyCollection<Microsoft365IndexedContent>> GetBySourceAsync(
        Guid organizationId,
        Guid sourceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<Microsoft365IndexedContent>>(
            contents.Where(content =>
                content.OrganizationId == organizationId
                && content.Microsoft365SourceId == sourceId).ToArray());

    public Task RequestAclReconciliationAsync(
        Guid sourceId,
        DateTimeOffset dueAt,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SaveAsync(
        Microsoft365IndexedContent content,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DeleteAsync(
        Microsoft365IndexedContent content,
        CancellationToken cancellationToken = default)
    {
        DeletedContents.Add(content);
        return Task.CompletedTask;
    }
}

internal sealed class StubReindexPassageIndexWriter : IMicrosoft365PassageIndexWriter
{
    public List<string> DeletedChunkIds { get; } = [];

    public Task MergeOrUploadAsync(
        Guid organizationId,
        IReadOnlyCollection<Microsoft365SearchPassage> passages,
        Microsoft365Acl acl,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DeleteAsync(
        IReadOnlyCollection<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        DeletedChunkIds.AddRange(chunkIds);
        return Task.CompletedTask;
    }
}
