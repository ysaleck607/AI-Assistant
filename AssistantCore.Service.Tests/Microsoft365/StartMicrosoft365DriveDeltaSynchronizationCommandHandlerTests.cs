using AssistantCore.Service.Application.Commands.StartMicrosoft365DriveDeltaSynchronization;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Services.Microsoft365;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class StartMicrosoft365DriveDeltaSynchronizationCommandHandlerTests
{
    [Theory, AutoDomainData]
    public async Task Given_ADeltaSynchronization_When_HandleAsync_Then_DelegatesToSynchronizationService(
        Guid sourceId,
        Guid synchronizationId,
        Microsoft365DriveDeltaSynchronizationResult expectedResult,
        CancellationToken cancellationToken)
    {
        // Given
        var service = new RecordingService(expectedResult);
        var handler = new StartMicrosoft365DriveDeltaSynchronizationCommandHandler(service);
        var command = new StartMicrosoft365DriveDeltaSynchronizationCommand(sourceId, synchronizationId);

        // When
        var result = await handler.HandleAsync(command, cancellationToken);

        // Then
        Assert.Same(expectedResult, result);
        Assert.Equal(sourceId, service.SourceId);
        Assert.Equal(synchronizationId, service.SynchronizationId);
        Assert.Equal(cancellationToken, service.CancellationToken);
    }

    private sealed class RecordingService(Microsoft365DriveDeltaSynchronizationResult result)
        : IMicrosoft365DriveSynchronizationService
    {
        public Guid? SourceId { get; private set; }

        public Guid? SynchronizationId { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<Microsoft365DriveInitialSynchronizationResult> StartInitialSynchronizationAsync(
            Guid sourceId,
            Guid synchronizationId,
            Guid? reindexOperationId = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Microsoft365DriveDeltaSynchronizationResult> StartDeltaSynchronizationAsync(
            Guid sourceId,
            Guid synchronizationId,
            CancellationToken cancellationToken = default)
        {
            SourceId = sourceId;
            SynchronizationId = synchronizationId;
            CancellationToken = cancellationToken;
            return Task.FromResult(result);
        }
    }
}
