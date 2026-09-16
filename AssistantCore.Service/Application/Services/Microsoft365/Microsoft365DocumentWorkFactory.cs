using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Models.Microsoft365;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public sealed class Microsoft365DocumentWorkFactory : IMicrosoft365DocumentWorkFactory
{
    public Microsoft365DocumentWorkData Create(
        Microsoft365Drive drive,
        Microsoft365DriveItemDelta item,
        DateTimeOffset createdAt,
        Guid? reindexOperationId = null)
    {
        ArgumentNullException.ThrowIfNull(drive);
        ArgumentNullException.ThrowIfNull(item);

        if (!item.IsDeleted && (!item.IsFile || string.IsNullOrWhiteSpace(item.Name)))
        {
            throw new InvalidOperationException(
                "Only a deleted item or an active Microsoft 365 file can create document work.");
        }

        var canonicalDriveId = string.IsNullOrWhiteSpace(item.CanonicalDriveId)
            ? drive.DriveId
            : item.CanonicalDriveId;
        var workType = item.IsDeleted
            ? Microsoft365DocumentWorkType.DeleteDocument
            : Microsoft365DocumentWorkType.ProcessDocument;
        var version = item.IsDeleted
            ? "delete"
            : !string.IsNullOrWhiteSpace(item.ETag)
                ? Microsoft365DocumentIndexVersion.Create(item.ETag)
                : throw new InvalidOperationException(
                    "An active Microsoft 365 file requires an eTag.");

        return new Microsoft365DocumentWorkData(
            drive.OrganizationId,
            workType,
            drive.SiteId,
            canonicalDriveId,
            item.Id,
            item.IsDeleted ? null : item.Name,
            item.IsDeleted ? null : item.ETag,
            item.CreatedDateTime,
            item.LastModifiedDateTime,
            item.WebUrl,
            item.IsDeleted ? null : item.Size,
            item.IsDeleted ? null : item.MimeType,
            CreateDeduplicationKey(
                drive.OrganizationId,
                canonicalDriveId,
                item.Id,
                version,
                reindexOperationId),
            createdAt);
    }

    /// <summary>
    /// La cle porte la reprise demandee par un operateur Synaptix lorsqu'il y en a une.
    /// Un ancien travail termine en <c>PermanentFailure</c> sur la meme version ne bloque donc
    /// plus la creation d'un nouveau travail, alors que deux lectures de la meme page a
    /// l'interieur de la reprise produisent toujours la meme cle.
    /// </summary>
    private static string CreateDeduplicationKey(
        Guid organizationId,
        string driveId,
        string itemId,
        string version,
        Guid? reindexOperationId)
    {
        List<string> segments =
        [
            organizationId.ToString("N"),
            driveId,
            itemId,
            version
        ];

        // Hors reprise, la cle reste exactement celle des travaux deja enregistres :
        // le segment supplementaire n'est ajoute que pour une reindexation administrative.
        if (reindexOperationId is { } operationId)
        {
            segments.Add(operationId.ToString("N"));
        }

        var identity = JsonSerializer.Serialize(segments);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }
}
