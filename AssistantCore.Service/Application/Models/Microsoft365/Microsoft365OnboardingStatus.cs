namespace AssistantCore.Service.Application.Models.Microsoft365;

public sealed record Microsoft365OnboardingStatus(
    bool IsAdministrator,
    string ConnectionStatus,
    bool IsConsentComplete,
    bool HasSelectedSite,
    bool HasIndexedSource,
    bool HasCompletedInitialSetup = false,
    bool IsEnvironmentReady = false)
{
    // This flag represents the one-time setup lifecycle, not transient indexing
    // health. Revoking consent resets the persisted completion marker.
    public bool IsComplete => IsConsentComplete && HasCompletedInitialSetup;
}
