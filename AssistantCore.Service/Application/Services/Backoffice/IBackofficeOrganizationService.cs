using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Services.Backoffice;

public interface IBackofficeOrganizationService
{
    Task<BackofficeOrganizationListResponse> SearchOrganizationsAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken = default);

    Task<BackofficeOrganizationDetailsDto> GetOrganizationDetailsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<BackofficeUserListResponse> GetUsersAsync(
        Guid organizationId,
        string? search,
        CancellationToken cancellationToken = default);

    Task<BackofficeUserDetailsDto> GetUserDetailsAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<BackofficeAccessReevaluationResultDto> ReevaluateUserAccessAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
