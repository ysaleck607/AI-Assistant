using AssistantCore.Repository.Domain.Entities;

namespace AssistantCore.Repository.Repositories;

public interface IMicrosoft365ConnectionRepository
{
    Task<Microsoft365Connection> PrepareConsentAsync(
        Guid organizationId,
        string stateHash,
        DateTimeOffset stateExpiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<Microsoft365Connection?> FindConsentAsync(
        Guid organizationId,
        string stateHash,
        CancellationToken cancellationToken = default);

    Task<bool> IsTenantConnectedToAnotherOrganizationAsync(
        Guid organizationId,
        string tenantId,
        CancellationToken cancellationToken = default);

    Task<Microsoft365Connection?> FindByIdAsync(
        Guid connectionId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<Microsoft365Connection?> FindForProcessingAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default);

    Task<Microsoft365Connection?> FindActiveByOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) => Task.FromResult<Microsoft365Connection?>(null);

    Task<Microsoft365Connection?> FindByOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) => Task.FromResult<Microsoft365Connection?>(null);

    Task CompleteConsentAsync(
        Microsoft365Connection connection,
        string tenantId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);

    Task CompleteOnboardingAsync(
        Guid organizationId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    Task MarkConsentErrorAsync(
        Microsoft365Connection connection,
        string errorCode,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);

    Task RevokeAsync(
        Microsoft365Connection connection,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);
}
