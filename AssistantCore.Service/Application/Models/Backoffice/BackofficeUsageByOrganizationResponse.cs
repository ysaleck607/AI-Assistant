using AssistantCore.Repository.Queries;

namespace AssistantCore.Service.Application.Models.Backoffice;

public sealed record BackofficeUsageByOrganizationDto(
    Guid OrganizationId,
    string OrganizationName,
    int ActiveUsers,
    int Requests)
{
    public static BackofficeUsageByOrganizationDto FromData(BackofficeUsageByOrganizationData data) =>
        new(data.OrganizationId, data.OrganizationName, data.ActiveUsers, data.Requests);
}

public sealed record BackofficeUsageByOrganizationResponse(IReadOnlyList<BackofficeUsageByOrganizationDto> Items)
{
    public static BackofficeUsageByOrganizationResponse FromData(IReadOnlyList<BackofficeUsageByOrganizationData> data) =>
        new(data.Select(BackofficeUsageByOrganizationDto.FromData).ToList());
}
