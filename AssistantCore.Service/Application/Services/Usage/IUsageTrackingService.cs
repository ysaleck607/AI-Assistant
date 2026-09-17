using AssistantCore.Service.Application.Models.Usage;

namespace AssistantCore.Service.Application.Services.Usage;

public interface IUsageTrackingService
{
    Task EnsureQuotaAvailableAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enregistre la consommation d'un message Assistant de maniere idempotente
    /// (un replay du meme message ne compte jamais deux fois), puis retourne le
    /// total actualise de la periode courante.
    /// </summary>
    Task<MessageUsageResponse> RecordConsumptionAsync(
        Guid organizationId,
        Guid assistantMessageId,
        long inputTokens,
        long outputTokens,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);

    Task<TokenUsageResponse> GetCurrentUsageAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);
}
