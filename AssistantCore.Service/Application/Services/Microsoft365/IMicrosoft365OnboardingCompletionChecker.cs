namespace AssistantCore.Service.Application.Services.Microsoft365;

/// <summary>
/// Repond uniquement a "le setup Microsoft 365 de cette organisation a-t-il deja ete termine ?".
/// La reponse repose sur la connexion active et le marqueur persistant de fin du premier setup;
/// elle ne depend pas de l'etat courant de l'indexation, d'AI Search ou des sources selectionnees.
/// Une revocation du consentement remet explicitement ce marqueur a zero.
/// </summary>
public interface IMicrosoft365OnboardingCompletionChecker
{
    Task<bool> IsCompleteAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
