using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Application.Services.Microsoft365;

namespace AssistantCore.Service.Application.Commands.GetMicrosoft365ReindexStatus;

public sealed class GetMicrosoft365ReindexStatusQueryHandler(
    IMicrosoft365ReindexStatusService reindexStatusService)
    : IRequestHandler<GetMicrosoft365ReindexStatusQuery, BackofficeMicrosoft365ReindexStatusResponse>
{
    public Task<BackofficeMicrosoft365ReindexStatusResponse> HandleAsync(
        GetMicrosoft365ReindexStatusQuery request,
        CancellationToken cancellationToken) =>
        reindexStatusService.GetStatusAsync(
            request.OrganizationId,
            request.OperationId,
            cancellationToken);
}
