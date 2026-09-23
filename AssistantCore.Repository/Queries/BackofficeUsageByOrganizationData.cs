namespace AssistantCore.Repository.Queries;

public sealed record BackofficeUsageByOrganizationData(
    Guid OrganizationId,
    string OrganizationName,
    int ActiveUsers,
    int Requests);
