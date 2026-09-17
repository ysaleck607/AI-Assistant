using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Service.Application.Commands.GetTokenUsage;
using AssistantCore.Service.Application.Models.Messages;
using AssistantCore.Service.Application.Models.Usage;
using AssistantCore.Service.Application.Services.Messages.Authorization;
using AssistantCore.Service.Application.Services.Usage;

namespace AssistantCore.Service.Tests.Usage;

public sealed class GetTokenUsageCommandHandlerTests
{
    [Theory, AutoDomainData]
    public async Task Given_TheCurrentOrganization_When_HandleAsync_Then_ReturnsItsUsageSummary(
        Organization organization,
        OrganizationMember member)
    {
        // Given
        var expectedResponse = new TokenUsageResponse(
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
            1_000_000,
            428_000,
            572_000,
            false);
        var usageTrackingService = new StubUsageTrackingService(expectedResponse);
        var handler = new GetTokenUsageCommandHandler(
            new StubUserContextService(new MessageUserContext(organization, member)),
            usageTrackingService);

        // When
        var response = await handler.HandleAsync(new GetTokenUsageCommand(), CancellationToken.None);

        // Then
        Assert.Same(expectedResponse, response);
        Assert.Equal(organization.Id, usageTrackingService.ReceivedOrganizationId);
    }

    private sealed class StubUserContextService(MessageUserContext context)
        : IMessageUserContextService
    {
        public Task<MessageUserContext> GetCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(context);
    }

    private sealed class StubUsageTrackingService(TokenUsageResponse response) : IUsageTrackingService
    {
        public Guid? ReceivedOrganizationId { get; private set; }

        public Task EnsureQuotaAvailableAsync(
            Guid organizationId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MessageUsageResponse> RecordConsumptionAsync(
            Guid organizationId,
            Guid assistantMessageId,
            long inputTokens,
            long outputTokens,
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TokenUsageResponse> GetCurrentUsageAsync(
            Guid organizationId,
            CancellationToken cancellationToken = default)
        {
            ReceivedOrganizationId = organizationId;
            return Task.FromResult(response);
        }
    }
}
