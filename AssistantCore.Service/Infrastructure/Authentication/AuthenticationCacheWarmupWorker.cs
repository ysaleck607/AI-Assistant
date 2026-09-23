using System.Collections.Concurrent;
using System.Threading.Channels;
using AssistantCore.Service.Application.Services.AuthenticateUser;
using AssistantCore.Service.Application.Services.Messages.Connectors.Microsoft365;
using AssistantCore.Service.Application.Services.Messages.Tools;
using AssistantCore.Service.Application.Services.Microsoft365;

namespace AssistantCore.Service.Infrastructure.Authentication;

public sealed class AuthenticationCacheWarmupWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<AuthenticationCacheWarmupWorker> logger)
    : BackgroundService, IAuthenticationCacheWarmupQueue
{
    private readonly Channel<WarmupRequest> queue = Channel.CreateBounded<WarmupRequest>(
        new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });
    private readonly ConcurrentDictionary<WarmupKey, byte> pending = new();

    public void TryQueue(
        Guid organizationId,
        string? externalTenantId,
        string entraUserId)
    {
        if (organizationId == Guid.Empty)
        {
            return;
        }

        var request = new WarmupRequest(
            organizationId,
            externalTenantId?.Trim(),
            entraUserId.Trim());
        var key = new WarmupKey(
            organizationId,
            request.ExternalTenantId?.ToLowerInvariant(),
            request.EntraUserId.ToLowerInvariant());

        if (!pending.TryAdd(key, 0))
        {
            return;
        }

        if (!queue.Writer.TryWrite(request))
        {
            pending.TryRemove(key, out _);
            logger.LogDebug(
                "Authentication cache warmup queue is full for organization {OrganizationId}.",
                organizationId);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
        {
            var key = new WarmupKey(
                request.OrganizationId,
                request.ExternalTenantId?.ToLowerInvariant(),
                request.EntraUserId.ToLowerInvariant());

            try
            {
                using var scope = scopeFactory.CreateScope();
                var toolRegistry = scope.ServiceProvider.GetRequiredService<IAiToolRegistry>();
                var outlookIndexingTask = EnsureCurrentUserOutlookIndexedAsync(
                    request,
                    stoppingToken);
                await EnsureCurrentUserOneDriveIndexedAsync(
                    scope.ServiceProvider,
                    request,
                    stoppingToken);
                await outlookIndexingTask;

                if (!string.IsNullOrWhiteSpace(request.ExternalTenantId)
                    && !string.IsNullOrWhiteSpace(request.EntraUserId))
                {
                    var groupResolver = scope.ServiceProvider
                        .GetRequiredService<IMicrosoft365UserGroupResolver>();
                    await groupResolver.ResolveGroupIdsAsync(
                        request.ExternalTenantId,
                        request.EntraUserId,
                        stoppingToken);
                }

                await toolRegistry.GetAvailableToolsAsync(
                    request.OrganizationId,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Authentication cache warmup failed for organization {OrganizationId}.",
                    request.OrganizationId);
            }
            finally
            {
                pending.TryRemove(key, out _);
            }
        }
    }

    private async Task EnsureCurrentUserOneDriveIndexedAsync(
        IServiceProvider serviceProvider,
        WarmupRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EntraUserId))
        {
            return;
        }

        try
        {
            var oneDriveIndexingService = serviceProvider
                .GetRequiredService<IMicrosoft365CurrentUserOneDriveIndexingService>();
            await oneDriveIndexingService.EnsureIndexedAsync(
                request.OrganizationId,
                request.EntraUserId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Automatic OneDrive indexing failed for organization {OrganizationId} and authenticated user {EntraUserId}.",
                request.OrganizationId,
                request.EntraUserId);
        }
    }

    private async Task EnsureCurrentUserOutlookIndexedAsync(
        WarmupRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EntraUserId))
        {
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var outlookIndexingService = scope.ServiceProvider
                .GetRequiredService<IMicrosoft365CurrentUserOutlookIndexingService>();
            await outlookIndexingService.EnsureIndexedAsync(
                request.OrganizationId,
                request.EntraUserId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Automatic Outlook indexing failed for organization {OrganizationId} and authenticated user {EntraUserId}.",
                request.OrganizationId,
                request.EntraUserId);
        }
    }

    private sealed record WarmupRequest(
        Guid OrganizationId,
        string? ExternalTenantId,
        string EntraUserId);

    private sealed record WarmupKey(
        Guid OrganizationId,
        string? ExternalTenantId,
        string EntraUserId);
}
