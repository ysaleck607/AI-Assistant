using System.Net;
using AssistantCore.ExternalServices.Services.Microsoft;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Infrastructure.Microsoft365;

public sealed class Microsoft365SubscriptionClientAdapter(
    MicrosoftIdentityClient identityClient,
    MicrosoftGraphSubscriptionClient subscriptionClient,
    IOptions<Microsoft365Options> options) : IMicrosoft365SubscriptionClient
{
    public async Task<Microsoft365SubscriptionResult> CreateAsync(
        string tenantId,
        string resource,
        string notificationUrl,
        DateTimeOffset expiresAt,
        string clientState,
        string changeType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var configuration = options.Value;
            var accessToken = await AcquireTokenAsync(configuration, tenantId, cancellationToken);
            var result = await subscriptionClient.CreateAsync(
                configuration.GraphBaseUrl,
                accessToken,
                resource,
                notificationUrl,
                expiresAt,
                clientState,
                changeType,
                cancellationToken);
            return new Microsoft365SubscriptionResult(result.Id, result.Resource, result.ExpiresAt);
        }
        catch (MicrosoftExternalException exception)
        {
            throw new Microsoft365ExternalException(
                "Microsoft Graph subscription could not be created.",
                exception);
        }
    }

    public async Task<Microsoft365SubscriptionRenewalResult> RenewAsync(
        string tenantId,
        string subscriptionId,
        string notificationUrl,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var configuration = options.Value;
            var accessToken = await AcquireTokenAsync(configuration, tenantId, cancellationToken);
            var result = await subscriptionClient.RenewAsync(
                configuration.GraphBaseUrl,
                accessToken,
                subscriptionId,
                notificationUrl,
                expiresAt,
                cancellationToken);
            return new Microsoft365SubscriptionRenewalResult(
                true,
                new Microsoft365SubscriptionResult(result.Id, result.Resource, result.ExpiresAt));
        }
        catch (MicrosoftExternalException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return Microsoft365SubscriptionRenewalResult.NotFound;
        }
        catch (MicrosoftExternalException exception) when (
            exception.StatusCode == HttpStatusCode.BadRequest
            && string.Equals(exception.ErrorCode, "ValidationError", StringComparison.OrdinalIgnoreCase)
            && exception.Message.Contains(
                "Subscription validation request failed",
                StringComparison.OrdinalIgnoreCase))
        {
            return Microsoft365SubscriptionRenewalResult.NotFound;
        }
        catch (MicrosoftExternalException exception)
        {
            throw new Microsoft365ExternalException(
                "Microsoft Graph subscription could not be renewed.",
                exception);
        }
    }

    public async Task DeleteAsync(
        string tenantId,
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var configuration = options.Value;
            var accessToken = await AcquireTokenAsync(configuration, tenantId, cancellationToken);
            await subscriptionClient.DeleteAsync(
                configuration.GraphBaseUrl,
                accessToken,
                subscriptionId,
                cancellationToken);
        }
        catch (MicrosoftExternalException exception)
        {
            throw new Microsoft365ExternalException(
                "Microsoft Graph subscription could not be deleted.",
                exception);
        }
    }

    private async Task<string> AcquireTokenAsync(
        Microsoft365Options configuration,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var token = await identityClient.AcquireApplicationTokenAsync(
            configuration.AuthorityBaseUrl,
            tenantId,
            configuration.ClientId,
            configuration.ClientSecret,
            cancellationToken);
        return token.AccessToken;
    }
}
