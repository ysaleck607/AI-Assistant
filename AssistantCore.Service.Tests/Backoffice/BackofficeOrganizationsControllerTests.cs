using System.Reflection;
using AssistantCore.Service.Application.Commands.GetBackofficeOrganizationDetails;
using AssistantCore.Service.Application.Commands.GetBackofficeOrganizations;
using AssistantCore.Service.Application.Commands.GetBackofficeOrganizationUserDetails;
using AssistantCore.Service.Application.Commands.GetBackofficeOrganizationUsers;
using AssistantCore.Service.Application.Commands.ReevaluateBackofficeUserAccess;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AssistantCore.Service.Tests.Backoffice;

public sealed class BackofficeOrganizationsControllerTests
{
    [Theory, AutoDomainData]
    public async Task Given_QueryParameters_When_GetOrganizations_Then_DispatchesQueryAndReturnsOk(
        CancellationToken cancellationToken,
        BackofficeOrganizationListResponse response)
    {
        // Given
        var dispatcher = new RecordingDispatcher { Response = response };
        var controller = new BackofficeOrganizationsController(dispatcher);

        // When
        var actionResult = await controller.GetOrganizations(2, 50, "metal", cancellationToken);

        // Then
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.Same(response, okResult.Value);
        var query = Assert.IsType<GetBackofficeOrganizationsQuery>(dispatcher.ReceivedRequest);
        Assert.Equal(2, query.Page);
        Assert.Equal(50, query.PageSize);
        Assert.Equal("metal", query.Search);
        Assert.Equal(cancellationToken, dispatcher.ReceivedCancellationToken);
    }

    [Theory, AutoDomainData]
    public async Task Given_OrganizationId_When_GetOrganizationDetails_Then_DispatchesQueryAndReturnsOk(
        CancellationToken cancellationToken,
        Guid organizationId,
        BackofficeOrganizationDetailsDto response)
    {
        // Given
        var dispatcher = new RecordingDispatcher { Response = response };
        var controller = new BackofficeOrganizationsController(dispatcher);

        // When
        var actionResult = await controller.GetOrganizationDetails(organizationId, cancellationToken);

        // Then
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.Same(response, okResult.Value);
        var query = Assert.IsType<GetBackofficeOrganizationDetailsQuery>(dispatcher.ReceivedRequest);
        Assert.Equal(organizationId, query.OrganizationId);
        Assert.Equal(cancellationToken, dispatcher.ReceivedCancellationToken);
    }

    [Theory, AutoDomainData]
    public async Task Given_OrganizationUser_When_GetUserDetails_Then_DispatchesTenantScopedQuery(
        CancellationToken cancellationToken,
        Guid organizationId,
        Guid userId,
        BackofficeUserDetailsDto response)
    {
        // Given
        var dispatcher = new RecordingDispatcher { Response = response };
        var controller = new BackofficeOrganizationsController(dispatcher);

        // When
        var actionResult = await controller.GetUserDetails(organizationId, userId, cancellationToken);

        // Then
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.Same(response, okResult.Value);
        var query = Assert.IsType<GetBackofficeOrganizationUserDetailsQuery>(dispatcher.ReceivedRequest);
        Assert.Equal(organizationId, query.OrganizationId);
        Assert.Equal(userId, query.UserId);
    }

    [Theory, AutoDomainData]
    public async Task Given_OrganizationUser_When_ReevaluateUserAccess_Then_DispatchesTenantScopedCommand(
        CancellationToken cancellationToken,
        Guid organizationId,
        Guid userId,
        BackofficeAccessReevaluationResultDto response)
    {
        // Given
        var dispatcher = new RecordingDispatcher { Response = response };
        var controller = new BackofficeOrganizationsController(dispatcher);

        // When
        var actionResult = await controller.ReevaluateUserAccess(organizationId, userId, cancellationToken);

        // Then
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.Same(response, okResult.Value);
        var command = Assert.IsType<ReevaluateBackofficeUserAccessCommand>(dispatcher.ReceivedRequest);
        Assert.Equal(organizationId, command.OrganizationId);
        Assert.Equal(userId, command.UserId);
    }

    [Theory, AutoDomainData]
    public void Given_Controller_When_InspectingAuthorization_Then_AllowsAnonymousAccess(int _)
    {
        // Given
        var controllerType = typeof(BackofficeOrganizationsController);

        // When
        var route = controllerType.GetCustomAttribute<RouteAttribute>();
        var authorize = controllerType.GetCustomAttribute<AuthorizeAttribute>();
        var allowAnonymous = controllerType.GetCustomAttribute<AllowAnonymousAttribute>();
        var usersMethod = controllerType.GetMethod(nameof(BackofficeOrganizationsController.GetUsers));
        var detailsMethod = controllerType.GetMethod(nameof(BackofficeOrganizationsController.GetUserDetails));
        var reevaluateMethod = controllerType.GetMethod(nameof(BackofficeOrganizationsController.ReevaluateUserAccess));

        // Then
        Assert.Equal("api/backoffice/organizations", route?.Template);
        Assert.Null(authorize);
        Assert.NotNull(allowAnonymous);
        Assert.Equal("{organizationId:guid}/users", usersMethod?.GetCustomAttribute<HttpGetAttribute>()?.Template);
        Assert.Equal(
            "{organizationId:guid}/users/{userId:guid}",
            detailsMethod?.GetCustomAttribute<HttpGetAttribute>()?.Template);
        Assert.Equal(
            "{organizationId:guid}/users/{userId:guid}/reevaluate-access",
            reevaluateMethod?.GetCustomAttribute<HttpPostAttribute>()?.Template);
    }
}
