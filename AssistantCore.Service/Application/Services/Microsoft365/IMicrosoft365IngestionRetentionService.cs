namespace AssistantCore.Service.Application.Services.Microsoft365;

public interface IMicrosoft365IngestionRetentionService
{
    Task<int> RunAsync(CancellationToken cancellationToken = default);
}
