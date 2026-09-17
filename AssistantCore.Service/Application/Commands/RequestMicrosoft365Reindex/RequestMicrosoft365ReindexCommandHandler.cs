using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Application.Services.Microsoft365;

namespace AssistantCore.Service.Application.Commands.RequestMicrosoft365Reindex;

public sealed class RequestMicrosoft365ReindexCommandHandler(
    IMicrosoft365ReindexService reindexService)
    : IRequestHandler<RequestMicrosoft365ReindexCommand, BackofficeMicrosoft365ReindexResponse>
{
    public Task<BackofficeMicrosoft365ReindexResponse> HandleAsync(
        RequestMicrosoft365ReindexCommand request,
        CancellationToken cancellationToken) =>
        reindexService.RequestReindexAsync(
            request.OrganizationId,
            request.Reason,
            cancellationToken);
}
