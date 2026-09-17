using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public interface IMicrosoft365ReindexStatusService
{
    Task<BackofficeMicrosoft365ReindexStatusResponse> GetStatusAsync(
        Guid organizationId,
        Guid operationId,
        CancellationToken cancellationToken = default);
}
