using AssistantCore.Service.Application.Models.Microsoft365;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public interface IMicrosoft365OutlookAttachmentClient
{
    Task<IReadOnlyCollection<Microsoft365OutlookFileAttachment>> GetFileAttachmentsAsync(
        string tenantId,
        string mailboxUserId,
        string messageId,
        CancellationToken cancellationToken = default);
}
