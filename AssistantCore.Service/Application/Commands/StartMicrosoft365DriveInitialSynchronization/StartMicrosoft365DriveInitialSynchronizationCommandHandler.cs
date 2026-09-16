using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Services.Microsoft365;

namespace AssistantCore.Service.Application.Commands.StartMicrosoft365DriveInitialSynchronization;

public sealed class StartMicrosoft365DriveInitialSynchronizationCommandHandler(
    IMicrosoft365DriveSynchronizationService synchronizationService)
    : IRequestHandler<StartMicrosoft365DriveInitialSynchronizationCommand, Microsoft365DriveInitialSynchronizationResult>
{
    public Task<Microsoft365DriveInitialSynchronizationResult> HandleAsync(
        StartMicrosoft365DriveInitialSynchronizationCommand request,
        CancellationToken cancellationToken) =>
        synchronizationService.StartInitialSynchronizationAsync(
            request.SourceId,
            request.SynchronizationId,
            cancellationToken: cancellationToken);
}
