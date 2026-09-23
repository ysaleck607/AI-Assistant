namespace AssistantCore.Service.Application.Exceptions;

/// <summary>
/// Never thrown to a caller - exists only to carry a human-readable message into an
/// OperationalIncident for the capacity alert (#1). Kept separate from
/// RequestRateLimitExceededException (the exception actually thrown to the HTTP caller)
/// so the digest email reads clearly instead of showing that exception's generic
/// user-facing message.
/// </summary>
public sealed class OrganizationCapacityAlertException(int organizationMessagesPerMinuteLimit)
    : Exception(
        $"Alerte de capacité : l'organisation a atteint sa limite de débit ({organizationMessagesPerMinuteLimit} msg/min).");
