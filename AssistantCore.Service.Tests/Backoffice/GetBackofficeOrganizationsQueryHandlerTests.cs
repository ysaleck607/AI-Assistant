using AssistantCore.Service.Application.Commands.GetBackofficeOrganizationDetails;
using AssistantCore.Service.Application.Commands.GetBackofficeOrganizations;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Application.Services.Backoffice;

namespace AssistantCore.Service.Tests.Backoffice;

public sealed class GetBackofficeOrganizationsQueryHandlerTests
{
    [Theory, AutoDomainData]
    public async Task Given_Query_When_HandleAsync_Then_ReturnsServiceResult(
        CancellationToken cancellationToken,
        BackofficeOrganizationListResponse response)
    {
        // Given
        var service = new StubBackofficeOrganizationService { ListResponse = response };
        var handler = new GetBackofficeOrganizationsQueryHandler(service);

        // When
        var result = await handler.HandleAsync(
            new GetBackofficeOrganizationsQuery(3, 10, "metal"),
            cancellationToken);

        // Then
        Assert.Same(response, result);
        Assert.Equal(3, service.ReceivedPage);
        Assert.Equal(10, service.ReceivedPageSize);
        Assert.Equal("metal", service.ReceivedSearch);
        Assert.Equal(cancellationToken, service.ReceivedCancellationToken);
    }

    [Theory, AutoDomainData]
    public async Task Given_Query_When_HandleAsync_Then_ReturnsDetailsServiceResult(
        CancellationToken cancellationToken,
        Guid organizationId,
        BackofficeOrganizationDetailsDto response)
    {
        // Given
        var service = new StubBackofficeOrganizationService { DetailsResponse = response };
        var handler = new GetBackofficeOrganizationDetailsQueryHandler(service);

        // When
        var result = await handler.HandleAsync(
            new GetBackofficeOrganizationDetailsQuery(organizationId),
            cancellationToken);

        // Then
        Assert.Same(response, result);
        Assert.Equal(organizationId, service.ReceivedOrganizationId);
        Assert.Equal(cancellationToken, service.ReceivedCancellationToken);
    }

    private sealed class StubBackofficeOrganizationService : IBackofficeOrganizationService
    {
        public BackofficeOrganizationListResponse? ListResponse { get; init; }

        public BackofficeOrganizationDetailsDto? DetailsResponse { get; init; }

        public int ReceivedPage { get; private set; }

        public int ReceivedPageSize { get; private set; }

        public string? ReceivedSearch { get; private set; }

        public Guid ReceivedOrganizationId { get; private set; }

        public CancellationToken ReceivedCancellationToken { get; private set; }

        public Task<BackofficeOrganizationListResponse> SearchOrganizationsAsync(
            int page,
            int pageSize,
            string? search,
            CancellationToken cancellationToken = default)
        {
            ReceivedPage = page;
            ReceivedPageSize = pageSize;
            ReceivedSearch = search;
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(ListResponse!);
        }

        public Task<BackofficeOrganizationDetailsDto> GetOrganizationDetailsAsync(
            Guid organizationId,
            CancellationToken cancellationToken = default)
        {
            ReceivedOrganizationId = organizationId;
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(DetailsResponse!);
        }

        public Task<BackofficeUserListResponse> GetUsersAsync(
            Guid organizationId,
            string? search,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<BackofficeUserDetailsDto> GetUserDetailsAsync(
            Guid organizationId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<BackofficeAccessReevaluationResultDto> ReevaluateUserAccessAsync(
            Guid organizationId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
