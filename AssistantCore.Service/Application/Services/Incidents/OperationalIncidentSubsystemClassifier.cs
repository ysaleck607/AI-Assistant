using AssistantCore.Repository.Abstractions;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Service.Application.Exceptions;

namespace AssistantCore.Service.Application.Services.Incidents;

/// <summary>
/// Associe un type d'exception a un sous-systeme et une severite pour #15. Les exceptions
/// metier normales (validation, ressource introuvable) ne sont volontairement pas mappees
/// ici : elles ne representent pas une panne de sous-systeme et ne doivent jamais generer
/// d'incident (voir OperationalIncidentReporter).
///
/// RequestRateLimitExceededException est l'exception a cette regle : elle est mappee
/// deliberement pour la feature #1 (alerte de capacite), reutilisant l'infrastructure
/// #15 (incident + digest email) faute d'un vrai concept de quota persistant cote backend.
/// </summary>
public static class OperationalIncidentSubsystemClassifier
{
    public static (OperationalIncidentSubsystem Subsystem, OperationalIncidentSeverity Severity) Classify(
        Exception exception) => exception switch
    {
        Microsoft365GraphTransientException => (OperationalIncidentSubsystem.MicrosoftGraph, OperationalIncidentSeverity.Warning),
        Microsoft365AclResolutionException => (OperationalIncidentSubsystem.MicrosoftGraph, OperationalIncidentSeverity.Error),
        Microsoft365ExternalException => (OperationalIncidentSubsystem.MicrosoftGraph, OperationalIncidentSeverity.Error),

        Microsoft365SourceAccessDeniedException => (OperationalIncidentSubsystem.AuthAndAccess, OperationalIncidentSeverity.Warning),
        UnauthorizedAccessException => (OperationalIncidentSubsystem.AuthAndAccess, OperationalIncidentSeverity.Warning),
        ForbiddenException => (OperationalIncidentSubsystem.AuthAndAccess, OperationalIncidentSeverity.Warning),

        // Par defaut SharePoint : le type ne distingue pas la source (SharePoint/OneDrive),
        // le futur appelant pourra forcer OneDrive via un override si necessaire.
        Microsoft365ContentExtractionException => (OperationalIncidentSubsystem.SharePoint, OperationalIncidentSeverity.Warning),

        AzureAiSearchUnavailableException => (OperationalIncidentSubsystem.AzureAiSearch, OperationalIncidentSeverity.Error),

        AiProviderTimeoutException => (OperationalIncidentSubsystem.FoundryLlm, OperationalIncidentSeverity.Warning),
        AiProviderLimitException => (OperationalIncidentSubsystem.FoundryLlm, OperationalIncidentSeverity.Warning),
        AiProviderUnavailableException => (OperationalIncidentSubsystem.FoundryLlm, OperationalIncidentSeverity.Error),
        AiProviderInvalidResponseException => (OperationalIncidentSubsystem.FoundryLlm, OperationalIncidentSeverity.Error),
        AiProviderException => (OperationalIncidentSubsystem.FoundryLlm, OperationalIncidentSeverity.Error),

        ExternalSourcesUnavailableException => (OperationalIncidentSubsystem.Application, OperationalIncidentSeverity.Error),

        RequestRateLimitExceededException => (OperationalIncidentSubsystem.Application, OperationalIncidentSeverity.Warning),

        _ => (OperationalIncidentSubsystem.Application, OperationalIncidentSeverity.Critical)
    };
}
