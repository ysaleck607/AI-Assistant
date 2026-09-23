namespace AssistantCore.Service.Application.Services.RateLimiting;

public interface IOrganizationOrchestrationLimitService
{
    Task<IAsyncDisposable> AcquireAsync(
        Guid organizationId,
        CancellationToken cancellationToken);
}
