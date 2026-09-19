using System.Globalization;
using System.Text.Json;
using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Microsoft365.Permissions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public sealed class Microsoft365ListItemProcessingService(
    IMicrosoft365ListItemWorkProcessingRepository workRepository,
    IMicrosoft365SourceDiscoveryRepository sourceDiscoveryRepository,
    IMicrosoft365IndexedContentRepository indexedContentRepository,
    IMicrosoft365DocumentChunkingService chunkingService,
    IMicrosoft365EmbeddingGenerator embeddingGenerator,
    IMicrosoft365AclResolver aclResolver,
    IMicrosoft365PassageIndexWriter indexWriter,
    IMicrosoft365ContentAclSynchronizationService aclSynchronizationService,
    IOptions<Microsoft365Options> options,
    TimeProvider timeProvider,
    ILogger<Microsoft365ListItemProcessingService> logger) : IMicrosoft365ListItemProcessingService
{
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var work = await workRepository.ClaimNextAsync(
            Guid.NewGuid(),
            now,
            now.AddMinutes(options.Value.DocumentWorkLeaseMinutes),
            cancellationToken);
        if (work is null)
        {
            return false;
        }

        try
        {
            if (work.WorkType == Microsoft365ListItemWorkType.DeleteListItem)
            {
                await DeleteAsync(work, cancellationToken);
            }
            else
            {
                await ProcessAsync(work, cancellationToken);
            }

            await workRepository.CompleteAsync(work, timeProvider.GetUtcNow(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Microsoft 365 list item work {WorkId} failed for item {ListItemId}.",
                work.Id,
                work.ListItemId);
            var failure = Microsoft365DocumentFailurePolicy.Evaluate(
                exception,
                work.AttemptCount,
                options.Value);
            var failedAt = timeProvider.GetUtcNow();
            await workRepository.FailAsync(
                work,
                failure.IsPermanent,
                exception.GetType().Name,
                failedAt,
                failedAt.Add(failure.RetryDelay),
                cancellationToken);
        }

        return true;
    }

    private async Task ProcessAsync(
        Microsoft365ListItemWork work,
        CancellationToken cancellationToken)
    {
        var source = work.Microsoft365Source;
        var connection = source.Microsoft365Connection;
        if (source.Kind != Microsoft365SourceKind.SharePointList
            || source.Status != Microsoft365SourceStatus.Enabled
            || !source.IsIndexed
            || connection.Status != Microsoft365ConnectionStatus.Active
            || string.IsNullOrWhiteSpace(connection.TenantId)
            || string.IsNullOrWhiteSpace(work.ETag)
            || string.IsNullOrWhiteSpace(work.FieldsJson))
        {
            throw new InvalidDataException("The Microsoft 365 list item work is no longer indexable.");
        }

        var documentVersion = Microsoft365DocumentIndexVersion.Create(work.ETag);
        var existing = await indexedContentRepository.FindAsync(
            work.OrganizationId,
            source.Id,
            work.ListItemId,
            cancellationToken);
        if (existing is { IsAvailable: true }
            && string.Equals(existing.DocumentVersion, documentVersion, StringComparison.Ordinal))
        {
            return;
        }

        var site = await sourceDiscoveryRepository.FindSiteAsync(
            work.OrganizationId,
            work.SiteId,
            cancellationToken);
        if (site is null || string.IsNullOrWhiteSpace(site.WebUrl))
        {
            throw new InvalidDataException("The Microsoft 365 list item's SharePoint site is unavailable.");
        }

        var reference = new Microsoft365ContentReference(
            Microsoft365ContentReferenceKind.ListItem,
            work.SiteId,
            DriveId: null,
            ListId: work.ListId,
            ItemId: work.ListItemId,
            SiteUrl: site.WebUrl);
        var resolution = await aclResolver.ResolveAsync(
            work.Organization,
            reference,
            cancellationToken);
        if (resolution is not Microsoft365AclResolution.ResolvedAcl resolved)
        {
            await aclSynchronizationService.MarkUnavailableIfRegisteredAsync(
                work.OrganizationId,
                source.Id,
                work.ListItemId,
                cancellationToken);
            throw new Microsoft365AclResolutionException();
        }

        var (title, content) = BuildIndexableContent(source.DisplayName, work.ListItemId, work.FieldsJson);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidDataException("The Microsoft 365 list item did not contain indexable fields.");
        }

        var passages = chunkingService.CreateTextChunks(
            work.OrganizationId,
            source.Id,
            work.ListItemId,
            documentVersion,
            title,
            work.WebUrl,
            work.LastModifiedDateTime,
            content,
            "microsoft-list",
            work.SiteId);
        if (passages.Count == 0)
        {
            throw new InvalidDataException("The Microsoft 365 list item did not produce indexable passages.");
        }

        var embeddings = await embeddingGenerator.CreateAsync(
            passages.Select(passage => $"Liste: {passage.Title}\n\n{passage.Content}").ToArray(),
            cancellationToken);
        if (embeddings.Count != passages.Count)
        {
            throw new InvalidOperationException("List item embedding generation returned an unexpected result.");
        }

        var embeddedPassages = passages
            .Select((passage, index) => passage with { ContentVector = embeddings[index] })
            .ToArray();
        var obsoleteChunkIds = existing?.Passages
            .Select(passage => passage.ChunkId)
            .ToHashSet(StringComparer.Ordinal)
            ?? [];

        await indexWriter.MergeOrUploadAsync(
            work.OrganizationId,
            embeddedPassages,
            resolved.Acl,
            cancellationToken);
        var currentChunkIds = embeddedPassages
            .Select(passage => passage.ChunkId)
            .ToHashSet(StringComparer.Ordinal);
        var obsolete = obsoleteChunkIds
            .Where(chunkId => !currentChunkIds.Contains(chunkId))
            .ToArray();
        if (obsolete.Length > 0)
        {
            await indexWriter.DeleteAsync(work.OrganizationId, obsolete, cancellationToken);
        }

        await aclSynchronizationService.RegisterAsync(
            work.OrganizationId,
            source.Id,
            work.ListItemId,
            embeddedPassages.Select(passage => passage.ChunkId).ToArray(),
            resolved.Acl.Fingerprint,
            site.WebUrl,
            cancellationToken);
        await aclSynchronizationService.SynchronizeAsync(
            work.OrganizationId,
            source.Id,
            work.ListItemId,
            resolved.Acl,
            cancellationToken);

        var indexed = await indexedContentRepository.FindAsync(
            work.OrganizationId,
            source.Id,
            work.ListItemId,
            cancellationToken) ?? throw new InvalidOperationException("Indexed list item was not registered.");
        indexed.DocumentVersion = documentVersion;
        indexed.Title = title;
        indexed.WebUrl = work.WebUrl;
        indexed.LastModifiedAt = work.LastModifiedDateTime;
        await indexedContentRepository.SaveAsync(indexed, cancellationToken);
    }

    private async Task DeleteAsync(
        Microsoft365ListItemWork work,
        CancellationToken cancellationToken)
    {
        var content = await indexedContentRepository.FindAsync(
            work.OrganizationId,
            work.Microsoft365SourceId,
            work.ListItemId,
            cancellationToken);
        if (content is null)
        {
            return;
        }

        var chunkIds = content.Passages
            .Select(passage => passage.ChunkId)
            .Where(chunkId => !string.IsNullOrWhiteSpace(chunkId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (chunkIds.Length > 0)
        {
            await indexWriter.DeleteAsync(work.OrganizationId, chunkIds, cancellationToken);
        }

        await indexedContentRepository.DeleteAsync(content, cancellationToken);
    }

    internal static (string Title, string Content) BuildIndexableContent(
        string listTitle,
        string itemId,
        string fieldsJson)
    {
        using var document = JsonDocument.Parse(fieldsJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Microsoft 365 list item fields must be a JSON object.");
        }

        var lines = new List<string>();
        Flatten(document.RootElement, prefix: null, lines);
        var itemTitle = document.RootElement.TryGetProperty("Title", out var titleElement)
            && titleElement.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(titleElement.GetString())
                ? titleElement.GetString()!.Trim()
                : $"élément {itemId}";
        var normalizedListTitle = string.IsNullOrWhiteSpace(listTitle)
            ? "Liste Microsoft 365"
            : listTitle.Trim();
        return ($"{normalizedListTitle} — {itemTitle}", string.Join(Environment.NewLine, lines));
    }

    private static void Flatten(JsonElement element, string? prefix, ICollection<string> lines)
    {
        foreach (var property in element.EnumerateObject())
        {
            var path = string.IsNullOrWhiteSpace(prefix)
                ? property.Name
                : $"{prefix}.{property.Name}";
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    Flatten(property.Value, path, lines);
                    break;
                case JsonValueKind.Array:
                    var values = property.Value.EnumerateArray()
                        .Select(ToDisplayValue)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .ToArray();
                    if (values.Length > 0)
                    {
                        lines.Add($"{path}: {string.Join(", ", values)}");
                    }
                    break;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    break;
                default:
                    var value = ToDisplayValue(property.Value);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        lines.Add($"{path}: {value}");
                    }
                    break;
            }
        }
    }

    private static string ToDisplayValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.TryGetDecimal(out var number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Object => value.GetRawText(),
        JsonValueKind.Array => value.GetRawText(),
        _ => string.Empty
    };
}
