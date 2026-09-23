using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Models.Microsoft365.Permissions;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public interface IMicrosoft365PassageIndexWriter
{
    Task MergeOrUploadAsync(
        Guid organizationId,
        IReadOnlyCollection<Microsoft365SearchPassage> passages,
        Microsoft365Acl acl,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid organizationId,
        IReadOnlyCollection<string> chunkIds,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("Passage deletion is not implemented."));
}
