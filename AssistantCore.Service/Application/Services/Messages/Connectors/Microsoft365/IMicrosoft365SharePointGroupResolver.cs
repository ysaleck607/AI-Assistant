namespace AssistantCore.Service.Application.Services.Messages.Connectors.Microsoft365;

public interface IMicrosoft365SharePointGroupResolver
{
    Task<IReadOnlyCollection<string>> ResolveGroupIdsAsync(
        Guid organizationId,
        string externalTenantId,
        string entraUserId,
        CancellationToken cancellationToken);
}
