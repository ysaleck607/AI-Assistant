using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AssistantCore.Repository.Abstractions;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Services.AuthenticateUser;
using AssistantCore.Service.Application.Services.Messages.Tools;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public sealed class Microsoft365ConnectionService(
    IAuthenticateUserService authenticateUserService,
    IMicrosoft365ConnectionRepository connectionRepository,
    IMicrosoft365ConsentClient consentClient,
    IMicrosoft365ConsentStateProtector stateProtector,
    IMicrosoft365TechnicalTokenStore tokenStore,
    IAuthenticationCacheWarmupQueue cacheWarmupQueue,
    IOptions<Microsoft365Options> options,
    TimeProvider timeProvider,
    IAiToolRegistry? toolRegistry = null) : IMicrosoft365ConnectionService
{
    public async Task<Uri> StartConsentAsync(CancellationToken cancellationToken = default)
    {
        var (organization, member) = await authenticateUserService.GetOrganizationAsync(cancellationToken);
        if (member.Role != OrganizationRole.Admin)
        {
            throw new ForbiddenException("Administrator access required.");
        }

        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(options.Value.ConsentStateLifetimeMinutes);
        var state = stateProtector.Protect(new Microsoft365ConsentState(
            organization.Id,
            Guid.NewGuid(),
            expiresAt,
            member.ExternalUserId));

        await connectionRepository.PrepareConsentAsync(
            organization.Id,
            ComputeStateHash(state),
            expiresAt,
            now,
            cancellationToken);

        return consentClient.CreateAdminConsentUri(state);
    }

    public async Task<Microsoft365ConsentCompletionResult> CompleteConsentAsync(
        string tenantId,
        bool adminConsent,
        string state,
        string? microsoftError,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            throw new Microsoft365ConsentException(
                "Microsoft 365 consent state is required.",
                Microsoft365ConsentException.AdminConsentIncomplete);
        }

        Microsoft365ConsentState consentState;
        try
        {
            consentState = stateProtector.Unprotect(state);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            throw new Microsoft365ConsentException(
                "Microsoft 365 consent state is invalid.",
                Microsoft365ConsentException.AdminConsentIncomplete);
        }

        var now = timeProvider.GetUtcNow();
        if (consentState.ExpiresAt <= now)
        {
            throw new Microsoft365ConsentException(
                "Microsoft 365 consent state has expired.",
                Microsoft365ConsentException.AdminConsentIncomplete);
        }

        var connection = await connectionRepository.FindConsentAsync(
            consentState.OrganizationId,
            ComputeStateHash(state),
            cancellationToken)
            ?? throw new Microsoft365ConsentException(
                "Microsoft 365 consent state is invalid or has already been replaced.",
                Microsoft365ConsentException.AdminConsentIncomplete);

        if (connection.ConsentStateConsumedAt is not null)
        {
            throw new Microsoft365ConsentException(
                "Microsoft 365 consent state has already been used.",
                Microsoft365ConsentException.AdminConsentIncomplete);
        }

        if (connection.ConsentStateExpiresAt is null || connection.ConsentStateExpiresAt <= now)
        {
            throw new Microsoft365ConsentException(
                "Microsoft 365 consent state has expired.",
                Microsoft365ConsentException.AdminConsentIncomplete);
        }

        if (!string.IsNullOrWhiteSpace(microsoftError) || !adminConsent)
        {
            await connectionRepository.MarkConsentErrorAsync(
                connection,
                "MicrosoftConsentDenied",
                now,
                cancellationToken);
            throw new Microsoft365ConsentException(
                "Microsoft 365 consent was not granted.",
                Microsoft365ConsentException.AdminConsentRefused);
        }

        if (!Guid.TryParse(tenantId, out var parsedTenantId) || parsedTenantId == Guid.Empty)
        {
            throw new Microsoft365ConsentException(
                "Microsoft 365 tenant is required.",
                Microsoft365ConsentException.AdminConsentIncomplete);
        }

        var validatedTenantId = parsedTenantId.ToString("D");
        Microsoft365ConsentExchange exchange;
        try
        {
            exchange = await consentClient.CompleteAdminConsentAsync(
                validatedTenantId,
                cancellationToken);
        }
        catch (Microsoft365ExternalException)
        {
            await connectionRepository.MarkConsentErrorAsync(
                connection,
                "MicrosoftAdminConsentValidationFailed",
                now,
                cancellationToken);
            throw new Microsoft365ConsentException(
                "Microsoft 365 consent could not be validated.",
                Microsoft365ConsentException.AdminConsentValidationFailed);
        }

        if (await connectionRepository.IsTenantConnectedToAnotherOrganizationAsync(
                consentState.OrganizationId,
                validatedTenantId,
                cancellationToken))
        {
            throw new Microsoft365ConsentException(
                "Microsoft 365 tenant is already connected to another organization.",
                Microsoft365ConsentException.WrongTenant);
        }

        bool permissionsVerified;
        try
        {
            permissionsVerified = await consentClient.VerifyRequiredPermissionsAsync(
                exchange.AccessToken,
                cancellationToken);
        }
        catch (Microsoft365ExternalException)
        {
            await connectionRepository.MarkConsentErrorAsync(
                connection,
                "MicrosoftAdminConsentValidationFailed",
                now,
                cancellationToken);
            throw new Microsoft365ConsentException(
                "Microsoft 365 consent could not be validated.",
                Microsoft365ConsentException.AdminConsentValidationFailed);
        }

        if (!permissionsVerified)
        {
            await connectionRepository.MarkConsentErrorAsync(
                connection,
                "MicrosoftRequiredPermissionsMissing",
                now,
                cancellationToken);
            throw new Microsoft365ConsentException(
                "Microsoft 365 required permissions are missing.",
                Microsoft365ConsentException.MissingRequiredPermissions);
        }

        await connectionRepository.CompleteConsentAsync(
            connection,
            validatedTenantId,
            now,
            cancellationToken);
        cacheWarmupQueue.TryQueue(
            consentState.OrganizationId,
            validatedTenantId,
            consentState.EntraUserId ?? string.Empty);
        toolRegistry?.InvalidateCache(connection.OrganizationId);
        try
        {
            await tokenStore.StoreAsync(
                connection.Id,
                exchange.AccessToken,
                exchange.AccessTokenExpiresAt,
                cancellationToken);
        }
        catch
        {
            await connectionRepository.MarkConsentErrorAsync(
                connection,
                "MicrosoftTechnicalTokenStorageFailed",
                now,
                cancellationToken);
            throw;
        }

        return new Microsoft365ConsentCompletionResult(
            connection.Id,
            validatedTenantId,
            connection.Status,
            new Uri(options.Value.ConsentSuccessRedirectUrl, UriKind.Absolute));
    }

    public async Task<Microsoft365ConnectionResult> RevokeAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        var (organization, member) = await authenticateUserService.GetOrganizationAsync(cancellationToken);
        if (member.Role != OrganizationRole.Admin)
        {
            throw new ForbiddenException("Administrator access required.");
        }

        var connection = await connectionRepository.FindByIdAsync(
            connectionId,
            organization.Id,
            cancellationToken)
            ?? throw new NotFoundException("Microsoft 365 connection was not found.");

        await connectionRepository.RevokeAsync(
            connection,
            timeProvider.GetUtcNow(),
            cancellationToken);
        await tokenStore.RemoveAsync(connection.Id, cancellationToken);

        return new Microsoft365ConnectionResult(
            connection.Id,
            connection.TenantId ?? string.Empty,
            connection.Status);
    }

    private static string ComputeStateHash(string state) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
}
