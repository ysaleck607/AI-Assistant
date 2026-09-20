using AssistantCore.ExternalServices.Entities.Azure;
using AssistantCore.ExternalServices.Services.Azure;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Microsoft365.Permissions;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Infrastructure.Microsoft365;

public sealed class Microsoft365PassageAclWriterAdapter(
    AzureAiSearchPassageAclClient client,
    IOptions<AzureAiSearchOptions> options) : IMicrosoft365PassageAclWriter
{
    public async Task SetAvailabilityAsync(
        IReadOnlyCollection<string> chunkIds,
        bool isAvailable,
        CancellationToken cancellationToken = default)
    {
        var configuration = GetConfiguration();
        try
        {
            await client.SetAvailabilityAsync(
                configuration.Endpoint,
                configuration.IndexName,
                configuration.ApiKey,
                chunkIds,
                isAvailable,
                cancellationToken);
        }
        catch (AzureAiSearchExternalException exception)
        {
            throw new AzureAiSearchUnavailableException(
                "Azure AI Search passage availability update failed.",
                exception);
        }
    }

    public async Task UpdateAclAsync(
        IReadOnlyCollection<string> chunkIds,
        Microsoft365Acl acl,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acl);
        var configuration = GetConfiguration();
        var updates = chunkIds.Select(chunkId => new AzureAiSearchPassageAclUpdate(
            chunkId,
            acl.AllowedEntraUserIds,
            acl.AllowedEntraGroupIds,
            acl.AllowedSharePointGroupIds,
            acl.HasAnonymousLink,
            acl.HasOrganizationLink,
            acl.Fingerprint)).ToArray();
        try
        {
            await client.UpdateAclAsync(
                configuration.Endpoint,
                configuration.IndexName,
                configuration.ApiKey,
                updates,
                cancellationToken);
        }
        catch (AzureAiSearchExternalException exception)
        {
            throw new AzureAiSearchUnavailableException(
                "Azure AI Search passage ACL update failed.",
                exception);
        }
    }

    private AzureAiSearchOptions GetConfiguration()
    {
        var configuration = options.Value;
        if (string.IsNullOrWhiteSpace(configuration.Endpoint)
            || string.IsNullOrWhiteSpace(configuration.IndexName))
        {
            throw new InvalidOperationException(
                "AzureSearch endpoint and index name are required for ACL synchronization.");
        }

        return configuration;
    }
}
