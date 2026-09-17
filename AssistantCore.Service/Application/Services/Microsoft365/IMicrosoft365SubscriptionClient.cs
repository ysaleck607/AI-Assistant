using AssistantCore.Service.Application.Models.Microsoft365;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public interface IMicrosoft365SubscriptionClient
{
    Task<Microsoft365SubscriptionResult> CreateAsync(
        string tenantId,
        string resource,
        string notificationUrl,
        DateTimeOffset expiresAt,
        string clientState,
        string changeType,
        CancellationToken cancellationToken = default);

    Task<Microsoft365SubscriptionRenewalResult> RenewAsync(
        string tenantId,
        string subscriptionId,
        string notificationUrl,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string tenantId,
        string subscriptionId,
        CancellationToken cancellationToken = default);
}
