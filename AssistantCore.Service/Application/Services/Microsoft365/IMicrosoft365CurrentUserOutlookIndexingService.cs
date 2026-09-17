namespace AssistantCore.Service.Application.Services.Microsoft365;

public interface IMicrosoft365CurrentUserOutlookIndexingService
{
    Task EnsureIndexedAsync(
        Guid organizationId,
        string entraUserId,
        CancellationToken cancellationToken = default);
}
