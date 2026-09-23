namespace AssistantCore.Service.Application.Exceptions;

/// <summary>
/// Never thrown to a caller - exists only to carry a human-readable message into an
/// OperationalIncident when a model's self-tracked monthly token consumption crosses
/// the configured alert threshold.
/// </summary>
public sealed class LlmQuotaAlertException(string model, double usageRatio, long tokensConsumed, long tokenLimit)
    : Exception(
        $"Alerte de quota : le modele {model} a atteint {usageRatio:P0} de son quota mensuel de tokens " +
        $"({tokensConsumed:N0} / {tokenLimit:N0}).");
