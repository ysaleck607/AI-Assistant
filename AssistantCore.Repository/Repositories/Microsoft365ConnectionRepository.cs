using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Repository.Repositories;

public sealed class Microsoft365ConnectionRepository(AssistantCoreDbContext dbContext)
    : IMicrosoft365ConnectionRepository
{
    public async Task<Microsoft365Connection> PrepareConsentAsync(
        Guid organizationId,
        string stateHash,
        DateTimeOffset stateExpiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var connector = await dbContext.OrganizationConnectors
            .SingleOrDefaultAsync(candidate =>
                candidate.OrganizationId == organizationId
                && candidate.Type == ConnectorType.Microsoft365,
                cancellationToken);

        if (connector is null)
        {
            connector = new OrganizationConnector
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                Type = ConnectorType.Microsoft365,
                Status = RecordStatus.Inactive,
                IsConfigured = false
            };
            dbContext.OrganizationConnectors.Add(connector);
        }

        var connection = await dbContext.Microsoft365Connections
            .SingleOrDefaultAsync(
                candidate => candidate.OrganizationId == organizationId,
                cancellationToken);

        if (connection is null)
        {
            connection = new Microsoft365Connection
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                OrganizationConnectorId = connector.Id,
                CreatedAt = now
            };
            dbContext.Microsoft365Connections.Add(connection);
        }

        connection.PrepareConsent(stateHash, stateExpiresAt, now);

        await dbContext.SaveChangesAsync(cancellationToken);
        return connection;
    }

    public Task<Microsoft365Connection?> FindConsentAsync(
        Guid organizationId,
        string stateHash,
        CancellationToken cancellationToken = default) =>
        dbContext.Microsoft365Connections
            .Include(connection => connection.OrganizationConnector)
            .SingleOrDefaultAsync(connection =>
                connection.OrganizationId == organizationId
                && connection.ConsentStateHash == stateHash,
                cancellationToken);

    public Task<bool> IsTenantConnectedToAnotherOrganizationAsync(
        Guid organizationId,
        string tenantId,
        CancellationToken cancellationToken = default) =>
        dbContext.Microsoft365Connections.AnyAsync(connection =>
            connection.OrganizationId != organizationId
            && connection.TenantId == tenantId,
            cancellationToken);

    public Task<Microsoft365Connection?> FindByIdAsync(
        Guid connectionId,
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        dbContext.Microsoft365Connections
            .Include(connection => connection.OrganizationConnector)
            .SingleOrDefaultAsync(connection =>
                connection.Id == connectionId
                && connection.OrganizationId == organizationId,
                cancellationToken);

    public Task<Microsoft365Connection?> FindForProcessingAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default) =>
        dbContext.Microsoft365Connections
            .AsNoTracking()
            .SingleOrDefaultAsync(connection => connection.Id == connectionId, cancellationToken);

    public Task<Microsoft365Connection?> FindActiveByOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        dbContext.Microsoft365Connections
            .Include(connection => connection.OrganizationConnector)
            .SingleOrDefaultAsync(connection =>
                connection.OrganizationId == organizationId
                && connection.Status == Microsoft365ConnectionStatus.Active,
                cancellationToken);

    public Task<Microsoft365Connection?> FindByOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        dbContext.Microsoft365Connections
            .AsNoTracking()
            .SingleOrDefaultAsync(
                connection => connection.OrganizationId == organizationId,
                cancellationToken);

    public async Task CompleteConsentAsync(
        Microsoft365Connection connection,
        string tenantId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        connection.Activate(tenantId, completedAt);
        connection.OrganizationConnector.Status = RecordStatus.Active;
        connection.OrganizationConnector.IsConfigured = true;

        var sharePointSource = await dbContext.OrganizationConnectorSources
            .SingleOrDefaultAsync(source =>
                source.OrganizationConnectorId == connection.OrganizationConnectorId
                && source.SourceType == Microsoft365SourceType.SharePoint,
                cancellationToken);
        if (sharePointSource is null)
        {
            dbContext.OrganizationConnectorSources.Add(new OrganizationConnectorSource
            {
                OrganizationConnectorId = connection.OrganizationConnectorId,
                SourceType = Microsoft365SourceType.SharePoint,
                Status = RecordStatus.Active,
                IsIndexed = true
            });
        }
        else
        {
            sharePointSource.Status = RecordStatus.Active;
            sharePointSource.IsIndexed = true;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteOnboardingAsync(
        Guid organizationId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        var connection = await dbContext.Microsoft365Connections
            .SingleOrDefaultAsync(candidate =>
                candidate.OrganizationId == organizationId
                && candidate.Status == Microsoft365ConnectionStatus.Active,
                cancellationToken);
        if (connection is null || connection.OnboardingCompletedAt is not null)
        {
            return;
        }

        connection.CompleteOnboarding(completedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkConsentErrorAsync(
        Microsoft365Connection connection,
        string errorCode,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        connection.MarkError(errorCode, occurredAt);
        connection.OrganizationConnector.Status = RecordStatus.Inactive;
        connection.OrganizationConnector.IsConfigured = false;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAsync(
        Microsoft365Connection connection,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        connection.Revoke(occurredAt);
        connection.OrganizationConnector.Status = RecordStatus.Inactive;
        connection.OrganizationConnector.IsConfigured = false;

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
