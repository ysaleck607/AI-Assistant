using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public sealed class Microsoft365SubscriptionMaintenanceService(
    IMicrosoft365SubscriptionRepository subscriptionRepository,
    IMicrosoft365SubscriptionClient subscriptionClient,
    IMicrosoft365ClientStateProtector clientStateProtector,
    IMicrosoft365SynchronizationPublisher synchronizationPublisher,
    IOptions<Microsoft365Options> options,
    TimeProvider timeProvider,
    ILogger<Microsoft365SubscriptionMaintenanceService> logger)
    : IMicrosoft365SubscriptionMaintenanceService
{
    private const string ReconciliationRequiredErrorCode = "ReconciliationRequired";
    private const string OperationFailedErrorCode = "MicrosoftGraphSubscriptionOperationFailed";
    private const string ClientStateMigrationRequiredErrorCode = "ClientStateMigrationRequired";

    public async Task RunMaintenanceAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var configuration = options.Value;
        var candidates = await subscriptionRepository.GetMaintenanceCandidatesAsync(
            now.AddHours(configuration.SubscriptionRenewalLeadTimeHours),
            cancellationToken);

        foreach (var subscription in candidates)
        {
            try
            {
                await ProcessAsync(subscription, now, configuration, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                MarkOperationFailed(subscription, now);
                await subscriptionRepository.SaveChangesAsync(cancellationToken);
                logger.LogError(
                    exception,
                    "Microsoft 365 subscription maintenance failed. SubscriptionRecordId: {SubscriptionRecordId}; SourceId: {SourceId}; Status: {Status}.",
                    subscription.Id,
                    subscription.Microsoft365SourceId,
                    subscription.Status);
            }
        }
    }

    private async Task ProcessAsync(
        Microsoft365Subscription subscription,
        DateTimeOffset now,
        Microsoft365Options configuration,
        CancellationToken cancellationToken)
    {
        if (subscription.Status == Microsoft365SubscriptionStatus.RevocationRequired)
        {
            await RevokeAsync(subscription, now, cancellationToken);
            return;
        }

        if (subscription.LastErrorCode == ReconciliationRequiredErrorCode)
        {
            await PublishReconciliationAsync(subscription, now, cancellationToken);
            return;
        }

        EnsureSourceCanBeSubscribed(subscription);

        if (subscription.LastErrorCode == ClientStateMigrationRequiredErrorCode)
        {
            await MigrateClientStateAsync(subscription, now, configuration, cancellationToken);
            return;
        }

        if (subscription.Status == Microsoft365SubscriptionStatus.Pending
            || string.IsNullOrWhiteSpace(subscription.MicrosoftSubscriptionId))
        {
            await CreateAsync(subscription, now, configuration, requestReconciliation: false, cancellationToken);
            return;
        }

        await RenewAsync(subscription, now, configuration, cancellationToken);
    }

    /// <summary>
    /// Un clientState protege par un algorithme desormais obsolete ne peut pas
    /// etre transforme vers le nouveau format (il est irreversible par
    /// construction). L'abonnement Microsoft Graph existant est donc supprime
    /// proprement puis recree avec un nouveau clientState, et une reconciliation
    /// complete est demandee pour couvrir toute notification manquee pendant la
    /// bascule.
    /// </summary>
    private async Task MigrateClientStateAsync(
        Microsoft365Subscription subscription,
        DateTimeOffset now,
        Microsoft365Options configuration,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(subscription.MicrosoftSubscriptionId))
        {
            await subscriptionClient.DeleteAsync(
                GetTenantId(subscription),
                subscription.MicrosoftSubscriptionId,
                cancellationToken);
        }

        subscription.MicrosoftSubscriptionId = null;
        subscription.ProtectedClientState = null;
        subscription.ExpiresAt = null;
        subscription.LastErrorCode = null;
        subscription.UpdatedAt = now;
        await subscriptionRepository.SaveChangesAsync(cancellationToken);

        await CreateAsync(subscription, now, configuration, requestReconciliation: true, cancellationToken);
    }

    private async Task CreateAsync(
        Microsoft365Subscription subscription,
        DateTimeOffset now,
        Microsoft365Options configuration,
        bool requestReconciliation,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId(subscription);
        var clientState = clientStateProtector.Create();
        subscription.ProtectedClientState = clientState.ProtectedValue;
        subscription.Status = requestReconciliation
            ? Microsoft365SubscriptionStatus.RenewalRequired
            : Microsoft365SubscriptionStatus.Pending;
        subscription.LastErrorCode = null;
        subscription.UpdatedAt = now;
        await subscriptionRepository.SaveChangesAsync(cancellationToken);

        var result = await subscriptionClient.CreateAsync(
            tenantId,
            subscription.Resource,
            BuildNotificationUrl(configuration),
            now.AddHours(configuration.SubscriptionLifetimeHours),
            clientState.Value,
            GetChangeType(subscription),
            cancellationToken);
        EnsureResourceMatches(subscription.Resource, result.Resource);

        subscription.MicrosoftSubscriptionId = result.SubscriptionId;
        subscription.ExpiresAt = result.ExpiresAt;
        subscription.LastRenewedAt = now;
        subscription.UpdatedAt = now;
        subscription.LastErrorCode = requestReconciliation
            ? ReconciliationRequiredErrorCode
            : null;
        subscription.Status = requestReconciliation
            ? Microsoft365SubscriptionStatus.RenewalRequired
            : Microsoft365SubscriptionStatus.Active;
        await subscriptionRepository.SaveChangesAsync(cancellationToken);

        if (requestReconciliation)
        {
            await PublishReconciliationAsync(subscription, now, cancellationToken);
        }
    }

    private async Task RenewAsync(
        Microsoft365Subscription subscription,
        DateTimeOffset now,
        Microsoft365Options configuration,
        CancellationToken cancellationToken)
    {
        subscription.Status = Microsoft365SubscriptionStatus.RenewalRequired;
        subscription.UpdatedAt = now;
        await subscriptionRepository.SaveChangesAsync(cancellationToken);

        var result = await subscriptionClient.RenewAsync(
            GetTenantId(subscription),
            subscription.MicrosoftSubscriptionId!,
            BuildNotificationUrl(configuration),
            now.AddHours(configuration.SubscriptionLifetimeHours),
            cancellationToken);
        if (!result.Exists)
        {
            subscription.MicrosoftSubscriptionId = null;
            subscription.ProtectedClientState = null;
            subscription.ExpiresAt = null;
            await subscriptionRepository.SaveChangesAsync(cancellationToken);
            await CreateAsync(subscription, now, configuration, requestReconciliation: true, cancellationToken);
            return;
        }

        var renewed = result.Subscription
            ?? throw new InvalidOperationException("Microsoft subscription renewal result was empty.");
        EnsureResourceMatches(subscription.Resource, renewed.Resource);
        subscription.ExpiresAt = renewed.ExpiresAt;
        subscription.LastRenewedAt = now;
        subscription.Status = Microsoft365SubscriptionStatus.Active;
        subscription.LastErrorCode = null;
        subscription.UpdatedAt = now;
        await subscriptionRepository.SaveChangesAsync(cancellationToken);
    }

    private async Task PublishReconciliationAsync(
        Microsoft365Subscription subscription,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var synchronization = subscriptionRepository.GetOrCreateDeltaSynchronization(subscription, now);
        await subscriptionRepository.SaveChangesAsync(cancellationToken);
        var work = Microsoft365SynchronizationWorkFactory.Create(subscription, synchronization)
            ?? throw new InvalidOperationException("Microsoft 365 source type cannot be synchronized.");

        await synchronizationPublisher.PublishAsync(work, cancellationToken);
        subscription.Status = Microsoft365SubscriptionStatus.Active;
        subscription.LastErrorCode = null;
        subscription.UpdatedAt = now;
        await subscriptionRepository.SaveChangesAsync(cancellationToken);
    }

    private async Task RevokeAsync(
        Microsoft365Subscription subscription,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(subscription.MicrosoftSubscriptionId))
        {
            await subscriptionClient.DeleteAsync(
                GetTenantId(subscription),
                subscription.MicrosoftSubscriptionId,
                cancellationToken);
        }

        subscription.Status = Microsoft365SubscriptionStatus.Revoked;
        subscription.ProtectedClientState = null;
        subscription.ExpiresAt = null;
        subscription.LastErrorCode = null;
        subscription.UpdatedAt = now;
        await subscriptionRepository.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureSourceCanBeSubscribed(Microsoft365Subscription subscription)
    {
        var source = subscription.Microsoft365Source;
        var connection = source.Microsoft365Connection;
        if (!source.IsIndexed
            || source.Status != Microsoft365SourceStatus.Enabled
            || connection.Status != Microsoft365ConnectionStatus.Active
            || connection.OrganizationConnector.Status != RecordStatus.Active)
        {
            throw new InvalidOperationException(
                "Only an enabled Microsoft 365 source attached to an active connector can be subscribed.");
        }
    }

    private static string GetTenantId(Microsoft365Subscription subscription) =>
        !string.IsNullOrWhiteSpace(subscription.Microsoft365Source.Microsoft365Connection.TenantId)
            ? subscription.Microsoft365Source.Microsoft365Connection.TenantId
            : throw new InvalidOperationException("Microsoft 365 connection tenant is missing.");

    private static string BuildNotificationUrl(Microsoft365Options configuration) =>
        $"{configuration.WebhookBaseUrl.TrimEnd('/')}/webhooks/microsoft-graph";

    private static string GetChangeType(Microsoft365Subscription subscription) =>
        subscription.Microsoft365Source.Kind == Microsoft365SourceKind.OutlookMailbox
            ? "created,updated,deleted"
            : "updated";

    private static void EnsureResourceMatches(string expected, string actual)
    {
        if (!string.Equals(expected.TrimStart('/'), actual.TrimStart('/'), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Microsoft Graph returned an unexpected subscription resource.");
        }
    }

    private static void MarkOperationFailed(
        Microsoft365Subscription subscription,
        DateTimeOffset occurredAt)
    {
        if (subscription.Status == Microsoft365SubscriptionStatus.Active)
        {
            subscription.Status = Microsoft365SubscriptionStatus.RenewalRequired;
        }

        subscription.LastErrorCode = OperationFailedErrorCode;
        subscription.UpdatedAt = occurredAt;
    }
}
