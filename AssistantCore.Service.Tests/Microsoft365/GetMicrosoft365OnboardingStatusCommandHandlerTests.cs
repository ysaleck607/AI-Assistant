using AssistantCore.Service.Application.Commands.GetMicrosoft365OnboardingStatus;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Services.Microsoft365;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class GetMicrosoft365OnboardingStatusCommandHandlerTests
{
    [Theory, AutoDomainData]
    public async Task Given_OnboardingStatus_When_HandleAsync_Then_ReturnsMappedStatus(
        CancellationToken cancellationToken)
    {
        // Given
        var status = new Microsoft365OnboardingStatus(
            IsAdministrator: true,
            ConnectionStatus: "Active",
            IsConsentComplete: true,
            HasSelectedSite: true,
            HasIndexedSource: true,
            HasCompletedInitialSetup: true);
        var service = new StubOnboardingService(status);
        var handler = new GetMicrosoft365OnboardingStatusCommandHandler(service);

        // When
        var response = await handler.HandleAsync(
            new GetMicrosoft365OnboardingStatusCommand(),
            cancellationToken);

        // Then
        Assert.True(response.IsAdministrator);
        Assert.True(response.IsComplete);
        Assert.Equal("Active", response.ConnectionStatus);
        Assert.Equal(cancellationToken, service.ReceivedCancellationToken);
    }

    private sealed class StubOnboardingService(Microsoft365OnboardingStatus status)
        : IMicrosoft365OnboardingService
    {
        public CancellationToken ReceivedCancellationToken { get; private set; }

        public Task<Microsoft365OnboardingStatus> GetStatusAsync(
            CancellationToken cancellationToken = default)
        {
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(status);
        }
    }
}
