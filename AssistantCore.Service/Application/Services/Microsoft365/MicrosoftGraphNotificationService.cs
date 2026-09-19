using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Models.Microsoft365;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public sealed class MicrosoftGraphNotificationService(
    IMicrosoft365SubscriptionRepository subscriptionRepository,
    IMicrosoft365IndexedContentRepository indexedContentRepository,
    IMicrosoft365ClientStateProtector clientStateProtector,
    TimeProvider timeProvider) : IMicrosoftGraphNotificationService
{
    public async Task HandleNotificationsAsync(
        IReadOnlyCollection<MicrosoftGraphNotification> notifications,
        CancellationToken cancellationToken = default)
    {
        var validNotifications = notifications
            .Where(notification =>
                !string.IsNullOrWhiteSpace(notification.SubscriptionId)
                && !string.IsNullOrWhiteSpace(notification.ClientState)
                && !string.IsNullOrWhiteSpace(notification.TenantId))
            .GroupBy(notification => notification.SubscriptionId, StringComparer.OrdinalIgnoreCase);

        foreach (var subscriptionNotifications in validNotifications)
        {
            var subscription = await subscriptionRepository.FindActiveForNotificationAsync(
                subscriptionNotifications.Key!,
                cancellationToken);
            var now = timeProvider.GetUtcNow();
            if (subscription is null
                || subscription.ExpiresAt is null
                || subscription.ExpiresAt <= now
                || string.IsNullOrWhiteSpace(subscription.ProtectedClientState))
            {
                continue;
            }

            var source = subscription.Microsoft365Source;
            var connection = source.Microsoft365Connection;
            if (!source.IsIndexed
                || source.Status != Microsoft365SourceStatus.Enabled
                || connection.Status != Microsoft365ConnectionStatus.Active
                || connection.OrganizationConnector.Status != RecordStatus.Active)
            {
                continue;
            }

            var hasTrustedNotification = subscriptionNotifications.Any(notification =>
                clientStateProtector.Matches(
                    notification.ClientState!,
                    subscription.ProtectedClientState)
                && string.Equals(
                    notification.TenantId,
                    connection.TenantId,
                    StringComparison.OrdinalIgnoreCase));

            if (!hasTrustedNotification)
            {
                continue;
            }

            await indexedContentRepository.RequestAclReconciliationAsync(
                source.Id,
                now,
                cancellationToken);
            subscriptionRepository.GetOrCreateDeltaSynchronization(
                subscription,
                now);
            source.NextSynchronizationAt = now;
            await subscriptionRepository.SaveChangesAsync(cancellationToken);
        }
    }
}
