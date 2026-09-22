namespace AssistantCore.Service.Application.Services.LlmQuota;

/// <summary>
/// Anti-spam for the LLM quota alert, keyed per model since the chat and embedding
/// models are tracked and alerted on independently.
/// </summary>
public interface ILlmQuotaAlertGate
{
    /// <returns>true the first time since the cooldown expired for this model, then false until it does again.</returns>
    bool TryAcquire(string model);
}
