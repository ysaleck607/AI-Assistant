using AssistantCore.Service.Application.Commands.GetMicrosoft365ReindexStatus;
using AssistantCore.Service.Application.Commands.RequestMicrosoft365Reindex;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Application.Services.Microsoft365;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365ReindexCommandHandlerTests
{
    [Theory, AutoDomainData]
    public async Task Given_ACommand_When_HandleAsync_Then_CallsOnlyTheReindexService(
        Guid organizationId,
        CancellationToken cancellationToken,
        BackofficeMicrosoft365ReindexResponse response)
    {
        // Given
        var service = new RecordingReindexService(response);
        var handler = new RequestMicrosoft365ReindexCommandHandler(service);

        // When
        var result = await handler.HandleAsync(
            new RequestMicrosoft365ReindexCommand(organizationId, "connector fixed"),
            cancellationToken);

        // Then
        Assert.Same(response, result);
        Assert.Equal(organizationId, service.OrganizationId);
        Assert.Equal("connector fixed", service.Reason);
        Assert.Equal(cancellationToken, service.CancellationToken);
    }

    [Theory, AutoDomainData]
    public async Task Given_AQuery_When_HandleAsync_Then_CallsOnlyTheReindexStatusService(
        Guid organizationId,
        Guid operationId,
        CancellationToken cancellationToken,
        BackofficeMicrosoft365ReindexStatusResponse response)
    {
        // Given
        var service = new RecordingReindexStatusService(response);
        var handler = new GetMicrosoft365ReindexStatusQueryHandler(service);

        // When
        var result = await handler.HandleAsync(
            new GetMicrosoft365ReindexStatusQuery(organizationId, operationId),
            cancellationToken);

        // Then
        Assert.Same(response, result);
        Assert.Equal(organizationId, service.OrganizationId);
        Assert.Equal(operationId, service.OperationId);
        Assert.Equal(cancellationToken, service.CancellationToken);
    }

    private sealed class RecordingReindexService(BackofficeMicrosoft365ReindexResponse response)
        : IMicrosoft365ReindexService
    {
        public Guid? OrganizationId { get; private set; }

        public string? Reason { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<BackofficeMicrosoft365ReindexResponse> RequestReindexAsync(
            Guid organizationId,
            string? reason,
            CancellationToken cancellationToken = default)
        {
            OrganizationId = organizationId;
            Reason = reason;
            CancellationToken = cancellationToken;
            return Task.FromResult(response);
        }
    }

    private sealed class RecordingReindexStatusService(
        BackofficeMicrosoft365ReindexStatusResponse response)
        : IMicrosoft365ReindexStatusService
    {
        public Guid? OrganizationId { get; private set; }

        public Guid? OperationId { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<BackofficeMicrosoft365ReindexStatusResponse> GetStatusAsync(
            Guid organizationId,
            Guid operationId,
            CancellationToken cancellationToken = default)
        {
            OrganizationId = organizationId;
            OperationId = operationId;
            CancellationToken = cancellationToken;
            return Task.FromResult(response);
        }
    }
}
