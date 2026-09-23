namespace AssistantCore.Service.Application.Services.RateLimiting;

/// <summary>
/// Anti-spam pour l'alerte de capacite (#1) : une organisation qui reste bloquee sur sa
/// limite de debit ne doit declencher qu'une alerte par fenetre de temps, pas une par
/// requete rejetee.
/// </summary>
public interface IOrganizationCapacityAlertGate
{
    /// <returns>true la premiere fois pour cette organisation, puis false jusqu'a la fin du cooldown.</returns>
    bool TryAcquire(Guid organizationId);
}
