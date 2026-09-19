namespace AssistantCore.Service.Application.Services.Microsoft365;

public interface IMicrosoft365ListItemProcessingService
{
    Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default);
}
