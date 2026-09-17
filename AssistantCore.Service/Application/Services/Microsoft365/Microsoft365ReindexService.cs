using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Queries;
using AssistantCore.Repository.Repositories;
using AssistantCore.Repository.Repositories.Audit;
using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Backoffice;
using Microsoft.Extensions.Logging;

namespace AssistantCore.Service.Application.Services.Microsoft365;

/// <summary>
/// Declenche la reprise complete demandee par un operateur Synaptix depuis l'interface
/// d'administration interne. Le service retrouve lui-meme la connexion et les bibliotheques
/// de l'organisation ciblee : aucun identifiant de source n'est accepte depuis la requete.
/// </summary>
public sealed class Microsoft365ReindexService(
    IBackofficeOrganizationQueries organizationQueries,
    IMicrosoft365ConnectionRepository connectionRepository,
    IMicrosoft365DriveRepository driveRepository,
    IMicrosoft365ReindexOperationRepository reindexOperationRepository,
    IAdministrativeAuditRepository administrativeAuditRepository,
    ICurrentIdentity currentIdentity,
    ICorrelationIdProvider correlationIdProvider,
    TimeProvider timeProvider,
    ILogger<Microsoft365ReindexService> logger) : IMicrosoft365ReindexService
{
    private const int MaximumReasonLength = 500;

    public async Task<BackofficeMicrosoft365ReindexResponse> RequestReindexAsync(
        Guid organizationId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty)
        {
            throw new BadRequestException("Organization identifier is required.");
        }

        var normalizedReason = NormalizeReason(reason);

        _ = await organizationQueries.GetOrganizationDetailsAsync(organizationId, cancellationToken)
            ?? throw new NotFoundException("Organization not found.");

        var connection = await connectionRepository.FindActiveByOrganizationAsync(
            organizationId,
            cancellationToken)
            ?? throw new NotFoundException("Active Microsoft 365 connection was not found.");

        var existingOperation = await reindexOperationRepository.FindActiveByOrganizationAsync(
            organizationId,
            cancellationToken);
        if (existingOperation is not null)
        {
            throw new ConflictException(
                "A Microsoft 365 reindex operation is already running for this organization.",
                ConflictException.Microsoft365ReindexAlreadyRunning);
        }

        var libraries = await GetReindexableLibrariesAsync(organizationId, connection, cancellationToken);
        if (libraries.Count == 0)
        {
            throw new BadRequestException(
                "The organization has no SharePoint library enabled for indexing.");
        }

        var requestedAt = timeProvider.GetUtcNow();
        var operation = new Microsoft365ReindexOperation
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Microsoft365ConnectionId = connection.Id,
            RequestedByOperatorId = GetOperatorId(),
            Reason = normalizedReason,
            Status = Microsoft365ReindexOperationStatus.Pending,
            SourceCount = libraries.Count,
            RequestedAt = requestedAt
        };

        administrativeAuditRepository.Stage(AdministrativeAuditEntryFactory.Create(
            organizationId,
            "ManagementAdmin",
            operation.RequestedByOperatorId,
            AdministrativeAuditAction.Microsoft365ReindexRequested,
            AdministrativeAuditSubjectTypes.Microsoft365Connection,
            connection.Id,
            requestedAt,
            new Dictionary<string, object?>(),
            new Dictionary<string, object?>
            {
                ["operationId"] = operation.Id,
                ["libraryCount"] = libraries.Count,
                ["reason"] = normalizedReason
            },
            correlationIdProvider.GetCorrelationId()));

        var createdOperation = await reindexOperationRepository.CreateAsync(
            operation,
            libraries.Select(library => library.Id).ToArray(),
            cancellationToken);
        if (createdOperation is null)
        {
            // Deux operateurs ont confirme au meme instant : la base a refuse la seconde
            // reprise et l'appelant recoit le meme conflit que sur la verification prealable.
            throw new ConflictException(
                "A Microsoft 365 reindex operation is already running for this organization.",
                ConflictException.Microsoft365ReindexAlreadyRunning);
        }

        logger.LogInformation(
            "Microsoft 365 reindex requested. OrganizationId={OrganizationId} OperationId={OperationId} "
            + "LibraryCount={LibraryCount} OperatorId={OperatorId}",
            organizationId,
            operation.Id,
            libraries.Count,
            operation.RequestedByOperatorId);

        return new BackofficeMicrosoft365ReindexResponse(
            operation.Id,
            organizationId,
            operation.Status.ToString(),
            libraries.Count,
            requestedAt);
    }

    /// <summary>
    /// Ne conserve que les bibliotheques rattachees a la connexion active de l'organisation
    /// demandee : une source appartenant a une autre organisation ne peut pas etre reprise.
    /// </summary>
    private async Task<IReadOnlyCollection<Microsoft365Drive>> GetReindexableLibrariesAsync(
        Guid organizationId,
        Microsoft365Connection connection,
        CancellationToken cancellationToken)
    {
        var drives = await driveRepository.GetIndexedSharePointDrivesAsync(
            organizationId,
            cancellationToken);

        return drives
            .Where(drive =>
                drive.OrganizationId == organizationId
                && drive.Microsoft365ConnectionId == connection.Id)
            .ToArray();
    }

    private Guid GetOperatorId()
    {
        var identity = currentIdentity.GetIdentity();
        return Guid.TryParse(identity.ExternalUserId, out var operatorId)
            ? operatorId
            : throw new InvalidOperationException(
                "The authenticated management user identifier must be a GUID to request a reindex.");
    }

    private static string? NormalizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var trimmed = reason.Trim();
        return trimmed.Length > MaximumReasonLength
            ? throw new BadRequestException(
                $"The reindex reason cannot exceed {MaximumReasonLength} characters.")
            : trimmed;
    }
}
