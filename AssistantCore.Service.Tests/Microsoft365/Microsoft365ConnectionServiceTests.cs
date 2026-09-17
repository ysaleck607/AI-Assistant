using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Services.AuthenticateUser;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365ConnectionServiceTests
{
    [Theory, AutoDomainData]
    public async Task Given_ValidConsentCallback_When_CompleteConsentAsync_Then_OneActiveConnectorIsCreated(
        Guid organizationId,
        Guid connectionId,
        Guid tenantId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        // Given
        var now = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
        var consentState = new Microsoft365ConsentState(organizationId, Guid.NewGuid(), now.AddMinutes(10));
        var connection = CreatePendingConnection(connectionId, organizationId, now.AddMinutes(10));
        var repository = new StubConnectionRepository { Connection = connection };
        var consentClient = new StubConsentClient
        {
            Exchange = new Microsoft365ConsentExchange(
                tenantId.ToString("D"),
                accessToken,
                now.AddHours(1))
        };
        var tokenStore = new RecordingTokenStore();
        var service = CreateService(
            repository,
            consentClient,
            new StubStateProtector { UnprotectedState = consentState },
            tokenStore,
            now);

        // When
        var result = await service.CompleteConsentAsync(
            tenantId.ToString("D"),
            true,
            "protected-state",
            null,
            cancellationToken);

        // Then
        Assert.Equal(connectionId, result.ConnectionId);
        Assert.Equal(tenantId.ToString("D"), result.TenantId);
        Assert.Equal(Microsoft365ConnectionStatus.Active, result.Status);
        Assert.Equal(
            "https://app.onpremia.example/microsoft365/consent/success",
            result.FrontendRedirectUri.AbsoluteUri);
        Assert.Equal(1, repository.CompleteConsentCallCount);
        Assert.Equal(connectionId, tokenStore.ConnectionId);
        Assert.Equal(accessToken, tokenStore.AccessToken);
        Assert.Equal(tenantId.ToString("D"), consentClient.ReceivedTenantId);
    }

    [Theory, AutoDomainData]
    public async Task Given_AdminConsentWasNotGranted_When_CompleteConsentAsync_Then_RequestIsRejected(
        Guid organizationId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        // Given
        var now = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
        var connection = CreatePendingConnection(Guid.NewGuid(), organizationId, now.AddMinutes(10));
        var consentClient = new StubConsentClient();
        var service = CreateService(
            new StubConnectionRepository { Connection = connection },
            consentClient,
            new StubStateProtector
            {
                UnprotectedState = new Microsoft365ConsentState(
                    organizationId,
                    Guid.NewGuid(),
                    now.AddMinutes(10))
            },
            new RecordingTokenStore(),
            now);

        // When
        var action = () => service.CompleteConsentAsync(
            tenantId.ToString("D"),
            false,
            "protected-state",
            null,
            cancellationToken);

        // Then
        var exception = await Assert.ThrowsAsync<Microsoft365ConsentException>(action);
        Assert.Equal(Microsoft365ConsentException.AdminConsentRefused, exception.ErrorCode);
        Assert.Null(consentClient.ReceivedTenantId);
    }

    [Theory, AutoDomainData]
    public async Task Given_ExpiredState_When_CompleteConsentAsync_Then_RequestIsRejected(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        // Given
        var now = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
        var stateProtector = new StubStateProtector
        {
            UnprotectedState = new Microsoft365ConsentState(organizationId, Guid.NewGuid(), now.AddSeconds(-1))
        };
        var service = CreateService(
            new StubConnectionRepository(),
            new StubConsentClient(),
            stateProtector,
            new RecordingTokenStore(),
            now);

        // When
        var action = () => service.CompleteConsentAsync(
            Guid.NewGuid().ToString("D"),
            true,
            "protected-state",
            null,
            cancellationToken);

        // Then
        var exception = await Assert.ThrowsAsync<Microsoft365ConsentException>(action);
        Assert.Equal(Microsoft365ConsentException.AdminConsentIncomplete, exception.ErrorCode);
    }

    [Theory, AutoDomainData]
    public async Task Given_ReplayedState_When_CompleteConsentAsync_Then_RequestIsRejected(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        // Given
        var now = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
        var connection = CreatePendingConnection(Guid.NewGuid(), organizationId, now.AddMinutes(10));
        connection.ConsentStateConsumedAt = now.AddMinutes(-1);
        var service = CreateService(
            new StubConnectionRepository { Connection = connection },
            new StubConsentClient(),
            new StubStateProtector
            {
                UnprotectedState = new Microsoft365ConsentState(organizationId, Guid.NewGuid(), now.AddMinutes(10))
            },
            new RecordingTokenStore(),
            now);

        // When
        var action = () => service.CompleteConsentAsync(
            Guid.NewGuid().ToString("D"),
            true,
            "protected-state",
            null,
            cancellationToken);

        // Then
        var exception = await Assert.ThrowsAsync<Microsoft365ConsentException>(action);
        Assert.Equal(Microsoft365ConsentException.AdminConsentIncomplete, exception.ErrorCode);
    }

    [Theory, AutoDomainData]
    public async Task Given_ModifiedState_When_CompleteConsentAsync_Then_RequestIsRejected(
        CancellationToken cancellationToken)
    {
        // Given
        var stateProtector = new StubStateProtector
        {
            UnprotectException = new CryptographicException("Invalid protected payload.")
        };
        var service = CreateService(
            new StubConnectionRepository(),
            new StubConsentClient(),
            stateProtector,
            new RecordingTokenStore(),
            DateTimeOffset.UtcNow);

        // When
        var action = () => service.CompleteConsentAsync(
            Guid.NewGuid().ToString("D"),
            true,
            "modified-state",
            null,
            cancellationToken);

        // Then
        var exception = await Assert.ThrowsAsync<Microsoft365ConsentException>(action);
        Assert.Equal(Microsoft365ConsentException.AdminConsentIncomplete, exception.ErrorCode);
    }

    [Theory, AutoDomainData]
    public async Task Given_TenantConnectedToAnotherOrganization_When_CompleteConsentAsync_Then_RequestIsRejected(
        Guid organizationId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        // Given
        var now = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
        var repository = new StubConnectionRepository
        {
            Connection = CreatePendingConnection(Guid.NewGuid(), organizationId, now.AddMinutes(10)),
            TenantConnectedToAnotherOrganization = true
        };
        var consentClient = new StubConsentClient
        {
            Exchange = new Microsoft365ConsentExchange(
                tenantId.ToString("D"),
                "access-token",
                now.AddHours(1))
        };
        var service = CreateService(
            repository,
            consentClient,
            new StubStateProtector
            {
                UnprotectedState = new Microsoft365ConsentState(organizationId, Guid.NewGuid(), now.AddMinutes(10))
            },
            new RecordingTokenStore(),
            now);

        // When
        var action = () => service.CompleteConsentAsync(
            tenantId.ToString("D"),
            true,
            "protected-state",
            null,
            cancellationToken);

        // Then
        var exception = await Assert.ThrowsAsync<Microsoft365ConsentException>(action);
        Assert.Equal(Microsoft365ConsentException.WrongTenant, exception.ErrorCode);
        Assert.Equal(0, repository.CompleteConsentCallCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_TokenAcquisitionFails_When_CompleteConsentAsync_Then_ValidationFailedIsThrown(
        Guid organizationId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        // Given
        var now = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
        var repository = new StubConnectionRepository
        {
            Connection = CreatePendingConnection(Guid.NewGuid(), organizationId, now.AddMinutes(10))
        };
        var consentClient = new StubConsentClient
        {
            CompleteAdminConsentException = new Microsoft365ExternalException("Token acquisition failed.")
        };
        var service = CreateService(
            repository,
            consentClient,
            new StubStateProtector
            {
                UnprotectedState = new Microsoft365ConsentState(organizationId, Guid.NewGuid(), now.AddMinutes(10))
            },
            new RecordingTokenStore(),
            now);

        // When
        var action = () => service.CompleteConsentAsync(
            tenantId.ToString("D"),
            true,
            "protected-state",
            null,
            cancellationToken);

        // Then
        var exception = await Assert.ThrowsAsync<Microsoft365ConsentException>(action);
        Assert.Equal(Microsoft365ConsentException.AdminConsentValidationFailed, exception.ErrorCode);
        Assert.Equal(0, repository.CompleteConsentCallCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_RequiredPermissionsAreMissing_When_CompleteConsentAsync_Then_MissingRequiredPermissionsIsThrown(
        Guid organizationId,
        Guid tenantId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        // Given
        var now = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
        var repository = new StubConnectionRepository
        {
            Connection = CreatePendingConnection(Guid.NewGuid(), organizationId, now.AddMinutes(10))
        };
        var consentClient = new StubConsentClient
        {
            Exchange = new Microsoft365ConsentExchange(tenantId.ToString("D"), accessToken, now.AddHours(1)),
            PermissionsVerified = false
        };
        var service = CreateService(
            repository,
            consentClient,
            new StubStateProtector
            {
                UnprotectedState = new Microsoft365ConsentState(organizationId, Guid.NewGuid(), now.AddMinutes(10))
            },
            new RecordingTokenStore(),
            now);

        // When
        var action = () => service.CompleteConsentAsync(
            tenantId.ToString("D"),
            true,
            "protected-state",
            null,
            cancellationToken);

        // Then
        var exception = await Assert.ThrowsAsync<Microsoft365ConsentException>(action);
        Assert.Equal(Microsoft365ConsentException.MissingRequiredPermissions, exception.ErrorCode);
        Assert.Equal(1, consentClient.VerifyPermissionsCallCount);
        Assert.Equal(accessToken, consentClient.ReceivedAccessToken);
        Assert.Equal(0, repository.CompleteConsentCallCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_PermissionVerificationFailsTechnically_When_CompleteConsentAsync_Then_ValidationFailedIsThrown(
        Guid organizationId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        // Given
        var now = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
        var repository = new StubConnectionRepository
        {
            Connection = CreatePendingConnection(Guid.NewGuid(), organizationId, now.AddMinutes(10))
        };
        var consentClient = new StubConsentClient
        {
            Exchange = new Microsoft365ConsentExchange(tenantId.ToString("D"), "access-token", now.AddHours(1)),
            VerifyPermissionsException = new Microsoft365ExternalException("Graph is unavailable.")
        };
        var service = CreateService(
            repository,
            consentClient,
            new StubStateProtector
            {
                UnprotectedState = new Microsoft365ConsentState(organizationId, Guid.NewGuid(), now.AddMinutes(10))
            },
            new RecordingTokenStore(),
            now);

        // When
        var action = () => service.CompleteConsentAsync(
            tenantId.ToString("D"),
            true,
            "protected-state",
            null,
            cancellationToken);

        // Then
        var exception = await Assert.ThrowsAsync<Microsoft365ConsentException>(action);
        Assert.Equal(Microsoft365ConsentException.AdminConsentValidationFailed, exception.ErrorCode);
        Assert.Equal(0, repository.CompleteConsentCallCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_PermissionsWereRevokedAfterInstallation_When_CompleteConsentAsyncRunsAgain_Then_MissingRequiredPermissionsIsThrown(
        Guid organizationId,
        Guid tenantId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        // Given: a connection that was already Active once, being re-consented
        // because its Microsoft-side permissions were revoked after installation.
        var now = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
        var connection = CreatePendingConnection(Guid.NewGuid(), organizationId, now.AddMinutes(10));
        connection.Status = Microsoft365ConnectionStatus.Active;
        connection.TenantId = tenantId.ToString("D");
        var repository = new StubConnectionRepository { Connection = connection };
        var consentClient = new StubConsentClient
        {
            Exchange = new Microsoft365ConsentExchange(tenantId.ToString("D"), accessToken, now.AddHours(1)),
            PermissionsVerified = false
        };
        var service = CreateService(
            repository,
            consentClient,
            new StubStateProtector
            {
                UnprotectedState = new Microsoft365ConsentState(organizationId, Guid.NewGuid(), now.AddMinutes(10))
            },
            new RecordingTokenStore(),
            now);

        // When
        var action = () => service.CompleteConsentAsync(
            tenantId.ToString("D"),
            true,
            "protected-state",
            null,
            cancellationToken);

        // Then
        var exception = await Assert.ThrowsAsync<Microsoft365ConsentException>(action);
        Assert.Equal(Microsoft365ConsentException.MissingRequiredPermissions, exception.ErrorCode);
        Assert.Equal(0, repository.CompleteConsentCallCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_NonAdministrator_When_StartConsentAsync_Then_AccessIsForbidden(
        Organization organization,
        OrganizationMember member,
        CancellationToken cancellationToken)
    {
        // Given
        member.Role = OrganizationRole.User;
        var authenticateUserService = new StubAuthenticateUserService { Result = (organization, member) };
        var service = CreateService(
            new StubConnectionRepository(),
            new StubConsentClient(),
            new StubStateProtector(),
            new RecordingTokenStore(),
            DateTimeOffset.UtcNow,
            authenticateUserService);

        // When
        var action = () => service.StartConsentAsync(cancellationToken);

        // Then
        await Assert.ThrowsAsync<AssistantCore.Repository.Abstractions.ForbiddenException>(action);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnActiveConnection_When_RevokeAsync_Then_ConnectionAndTokenAreRevoked(
        Organization organization,
        OrganizationMember member,
        Guid connectionId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        // Given
        member.OrganizationId = organization.Id;
        member.Role = OrganizationRole.Admin;
        var connection = new Microsoft365Connection
        {
            Id = connectionId,
            OrganizationId = organization.Id,
            TenantId = tenantId,
            Status = Microsoft365ConnectionStatus.Active,
            OrganizationConnector = new OrganizationConnector
            {
                Status = RecordStatus.Active,
                IsConfigured = true
            }
        };
        var repository = new StubConnectionRepository { Connection = connection };
        var tokenStore = new RecordingTokenStore();
        var service = CreateService(
            repository,
            new StubConsentClient(),
            new StubStateProtector(),
            tokenStore,
            DateTimeOffset.UtcNow,
            new StubAuthenticateUserService { Result = (organization, member) });

        // When
        var result = await service.RevokeAsync(connectionId, cancellationToken);

        // Then
        Assert.Equal(Microsoft365ConnectionStatus.Revoked, result.Status);
        Assert.Equal(connectionId, tokenStore.RemovedConnectionId);
        Assert.Equal(RecordStatus.Inactive, connection.OrganizationConnector.Status);
        Assert.False(connection.OrganizationConnector.IsConfigured);
    }

    private static Microsoft365ConnectionService CreateService(
        StubConnectionRepository repository,
        StubConsentClient consentClient,
        StubStateProtector stateProtector,
        RecordingTokenStore tokenStore,
        DateTimeOffset now,
        IAuthenticateUserService? authenticateUserService = null) =>
        new(
            authenticateUserService ?? CreateDefaultAuthenticateUserService(),
            repository,
            consentClient,
            stateProtector,
            tokenStore,
            new NoOpAuthenticationCacheWarmupQueue(),
            Options.Create(new Microsoft365Options
            {
                ConsentStateLifetimeMinutes = 10,
                ConsentSuccessRedirectUrl =
                    "https://app.onpremia.example/microsoft365/consent/success"
            }),
            new FixedTimeProvider(now));

    private sealed class NoOpAuthenticationCacheWarmupQueue : IAuthenticationCacheWarmupQueue
    {
        public void TryQueue(Guid organizationId, string? externalTenantId, string entraUserId)
        {
        }
    }

    private static IAuthenticateUserService CreateDefaultAuthenticateUserService()
    {
        var organization = new Organization { Id = Guid.NewGuid() };
        var member = new OrganizationMember
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            Role = OrganizationRole.Admin
        };
        return new StubAuthenticateUserService { Result = (organization, member) };
    }

    private static Microsoft365Connection CreatePendingConnection(
        Guid connectionId,
        Guid organizationId,
        DateTimeOffset expiresAt) =>
        new()
        {
            Id = connectionId,
            OrganizationId = organizationId,
            Status = Microsoft365ConnectionStatus.PendingConsent,
            ConsentStateExpiresAt = expiresAt,
            OrganizationConnector = new OrganizationConnector()
        };

    private sealed class StubConnectionRepository : IMicrosoft365ConnectionRepository
    {
        public Microsoft365Connection? Connection { get; init; }
        public bool TenantConnectedToAnotherOrganization { get; init; }
        public int CompleteConsentCallCount { get; private set; }

        public Task<Microsoft365Connection> PrepareConsentAsync(Guid organizationId, string stateHash, DateTimeOffset stateExpiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Task.FromResult(Connection ?? new Microsoft365Connection());

        public Task<Microsoft365Connection?> FindConsentAsync(Guid organizationId, string stateHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(Connection);

        public Task<bool> IsTenantConnectedToAnotherOrganizationAsync(Guid organizationId, string tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TenantConnectedToAnotherOrganization);

        public Task<Microsoft365Connection?> FindByIdAsync(Guid connectionId, Guid organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Connection?.Id == connectionId && Connection.OrganizationId == organizationId
                ? Connection
                : null);

        public Task<Microsoft365Connection?> FindForProcessingAsync(Guid connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Connection?.Id == connectionId ? Connection : null);

        public Task CompleteConsentAsync(Microsoft365Connection connection, string tenantId, DateTimeOffset completedAt, CancellationToken cancellationToken = default)
        {
            CompleteConsentCallCount++;
            connection.TenantId = tenantId;
            connection.Status = Microsoft365ConnectionStatus.Active;
            return Task.CompletedTask;
        }

        public Task MarkConsentErrorAsync(Microsoft365Connection connection, string errorCode, DateTimeOffset occurredAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RevokeAsync(Microsoft365Connection connection, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
        {
            connection.Revoke(occurredAt);
            connection.OrganizationConnector.Status = RecordStatus.Inactive;
            connection.OrganizationConnector.IsConfigured = false;
            return Task.CompletedTask;
        }
    }

    private sealed class StubConsentClient : IMicrosoft365ConsentClient
    {
        public Microsoft365ConsentExchange Exchange { get; init; } =
            new(Guid.NewGuid().ToString("D"), "access-token", DateTimeOffset.UtcNow.AddHours(1));
        public Exception? CompleteAdminConsentException { get; init; }
        public bool PermissionsVerified { get; init; } = true;
        public Exception? VerifyPermissionsException { get; init; }
        public string? ReceivedTenantId { get; private set; }
        public string? ReceivedAccessToken { get; private set; }
        public int VerifyPermissionsCallCount { get; private set; }

        public Uri CreateAdminConsentUri(string state) =>
            new("https://login.microsoftonline.com/organizations/v2.0/adminconsent");

        public Task<Microsoft365ConsentExchange> CompleteAdminConsentAsync(
            string tenantId,
            CancellationToken cancellationToken = default)
        {
            ReceivedTenantId = tenantId;

            if (CompleteAdminConsentException is not null)
            {
                return Task.FromException<Microsoft365ConsentExchange>(CompleteAdminConsentException);
            }

            return Task.FromResult(Exchange);
        }

        public Task<bool> VerifyRequiredPermissionsAsync(
            string accessToken,
            CancellationToken cancellationToken = default)
        {
            VerifyPermissionsCallCount++;
            ReceivedAccessToken = accessToken;

            if (VerifyPermissionsException is not null)
            {
                return Task.FromException<bool>(VerifyPermissionsException);
            }

            return Task.FromResult(PermissionsVerified);
        }
    }

    private sealed class StubStateProtector : IMicrosoft365ConsentStateProtector
    {
        public Microsoft365ConsentState UnprotectedState { get; init; } =
            new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(10));
        public Exception? UnprotectException { get; init; }

        public string Protect(Microsoft365ConsentState state) => "protected-state";

        public Microsoft365ConsentState Unprotect(string protectedState)
        {
            if (UnprotectException is not null)
            {
                throw UnprotectException;
            }

            return UnprotectedState;
        }
    }

    private sealed class RecordingTokenStore : IMicrosoft365TechnicalTokenStore
    {
        public Guid? ConnectionId { get; private set; }
        public string? AccessToken { get; private set; }
        public Guid? RemovedConnectionId { get; private set; }

        public Task StoreAsync(Guid connectionId, string accessToken, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
        {
            ConnectionId = connectionId;
            AccessToken = accessToken;
            return Task.CompletedTask;
        }

        public Task<string?> GetAsync(Guid connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(AccessToken);

        public Task RemoveAsync(Guid connectionId, CancellationToken cancellationToken = default)
        {
            RemovedConnectionId = connectionId;
            AccessToken = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
