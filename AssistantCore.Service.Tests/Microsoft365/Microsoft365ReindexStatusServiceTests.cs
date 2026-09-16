using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Services.Microsoft365;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365ReindexStatusServiceTests
{
    [Theory, AutoDomainData]
    public async Task Given_AKnownOperation_When_GetStatusAsync_Then_ReturnsItsProgressAndDates(
        Guid organizationId,
        Guid operationId,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken)
    {
        // Given
        var operation = new Microsoft365ReindexOperation
        {
            Id = operationId,
            OrganizationId = organizationId,
            Status = Microsoft365ReindexOperationStatus.Running,
            SourceCount = 4,
            CompletedSourceCount = 1,
            DiscoveredDocumentCount = 30,
            ProcessedDocumentCount = 12,
            IgnoredDocumentCount = 3,
            FailedDocumentCount = 2,
            RequestedAt = requestedAt,
            StartedAt = requestedAt.AddMinutes(1),
            LastErrorCode = "MicrosoftGraphDriveAccessDenied",
            Reason = "connector fixed"
        };
        var service = new Microsoft365ReindexStatusService(
            new StubReindexOperationRepository { KnownOperation = operation });

        // When
        var response = await service.GetStatusAsync(organizationId, operationId, cancellationToken);

        // Then
        Assert.Equal(operationId, response.OperationId);
        Assert.Equal(organizationId, response.OrganizationId);
        Assert.Equal("Running", response.Status);
        Assert.Equal(1, response.CompletedLibraryCount);
        Assert.Equal(4, response.LibraryCount);
        Assert.Equal(30, response.DiscoveredDocumentCount);
        Assert.Equal(12, response.ProcessedDocumentCount);
        Assert.Equal(3, response.IgnoredDocumentCount);
        Assert.Equal(2, response.FailedDocumentCount);
        Assert.Equal(requestedAt, response.RequestedAt);
        Assert.Equal(requestedAt.AddMinutes(1), response.StartedAt);
        Assert.Null(response.CompletedAt);
        Assert.Equal("MicrosoftGraphDriveAccessDenied", response.LastErrorCode);
        Assert.Equal("connector fixed", response.Reason);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnOperationOfAnotherOrganization_When_GetStatusAsync_Then_ThrowsNotFound(
        Guid organizationId,
        Guid otherOrganizationId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        // Given
        var operation = new Microsoft365ReindexOperation
        {
            Id = operationId,
            OrganizationId = otherOrganizationId,
            Status = Microsoft365ReindexOperationStatus.Running
        };
        var service = new Microsoft365ReindexStatusService(
            new StubReindexOperationRepository { KnownOperation = operation });

        // When
        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => service.GetStatusAsync(organizationId, operationId, cancellationToken));

        // Then
        Assert.Equal("Microsoft 365 reindex operation was not found.", exception.Message);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnEmptyOperationIdentifier_When_GetStatusAsync_Then_ThrowsBadRequest(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        // Given
        var service = new Microsoft365ReindexStatusService(new StubReindexOperationRepository());

        // When
        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => service.GetStatusAsync(organizationId, Guid.Empty, cancellationToken));

        // Then
        Assert.Equal("Reindex operation identifier is required.", exception.Message);
    }
}
