using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Services.AuthenticateUser;
using AssistantCore.Service.Application.Services.Messages.Tools;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public sealed class Microsoft365OnboardingService(
    IAuthenticateUserService authenticateUserService,
    IMicrosoft365ConnectionRepository connectionRepository,
    IMicrosoft365SourceDiscoveryRepository sourceRepository,
    TimeProvider timeProvider,
    IAiToolRegistry? toolRegistry = null)
    : IMicrosoft365OnboardingService
{
    public async Task<Microsoft365OnboardingStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var (organization, member) =
            await authenticateUserService.GetOrganizationAsync(cancellationToken);
        var connection = await connectionRepository.FindByOrganizationAsync(
            organization.Id,
            cancellationToken);
        if (connection is null
            || connection.Status != Microsoft365ConnectionStatus.Active)
        {
            return new Microsoft365OnboardingStatus(
                member.Role == OrganizationRole.Admin,
                connection?.Status.ToString() ?? "NotStarted",
                IsConsentComplete: false,
                HasSelectedSite: false,
                HasIndexedSource: false,
                HasCompletedInitialSetup: false);
        }

        var selectedSiteIds = await sourceRepository.GetSiteIdsAsync(
            organization.Id,
            cancellationToken);
        var hasSelectedSite = selectedSiteIds.Count > 0;
        var hasCompletedInitialSetup = connection.OnboardingCompletedAt is not null;

        if (!hasCompletedInitialSetup && hasSelectedSite)
        {
            await connectionRepository.CompleteOnboardingAsync(
                organization.Id,
                timeProvider.GetUtcNow(),
                cancellationToken);
            hasCompletedInitialSetup = true;
        }

        var hasIndexedSource = hasSelectedSite
            && await sourceRepository.HasIndexedSourceAsync(
                organization.Id,
                cancellationToken);
        var isEnvironmentReady = hasIndexedSource
            && await sourceRepository.IsEnvironmentReadyAsync(
                organization.Id,
                cancellationToken);
        var isEnvironmentReadyWithTools = isEnvironmentReady
            && toolRegistry is not null
            && (await toolRegistry.GetAvailableToolsAsync(
                organization.Id,
                cancellationToken)).Count > 0;

        return new Microsoft365OnboardingStatus(
            member.Role == OrganizationRole.Admin,
            connection.Status.ToString(),
            IsConsentComplete: true,
            HasSelectedSite: hasSelectedSite,
            HasIndexedSource: hasIndexedSource,
            HasCompletedInitialSetup: hasCompletedInitialSetup,
            IsEnvironmentReady: isEnvironmentReadyWithTools);
    }
}
