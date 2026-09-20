using System.Reflection;
using AssistantCore.Service.Application.Commands.GetBackofficeMessageWarnings;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Controllers;
using AssistantCore.Service.Infrastructure.Authentication.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AssistantCore.Service.Tests.Backoffice;

public sealed class BackofficeMessageQualityControllerTests
{
    [Theory, AutoDomainData]
    public async Task Given_Filters_When_GetWarnings_Then_DispatchesTheQueryAndReturnsOk(
        Guid organizationId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken,
        BackofficeMessageWarningListResponse response)
    {
        // Given
        var dispatcher = new RecordingDispatcher { Response = response };
        var controller = new BackofficeMessageQualityController(dispatcher);

        // When
        var actionResult = await controller.GetWarnings(
            page: 2,
            pageSize: 10,
            organizationId: organizationId,
            from: from,
            to: to,
            cancellationToken: cancellationToken);

        // Then
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.Same(response, okResult.Value);
        var query = Assert.IsType<GetBackofficeMessageWarningsQuery>(dispatcher.ReceivedRequest);
        Assert.Equal(2, query.Page);
        Assert.Equal(10, query.PageSize);
        Assert.Equal(organizationId, query.OrganizationId);
        Assert.Equal(from, query.From);
        Assert.Equal(to, query.To);
        Assert.Equal(cancellationToken, dispatcher.ReceivedCancellationToken);
    }

    [Fact]
    public void Given_Controller_When_InspectingAuthorization_Then_RequiresTheManagementAdminPolicy()
    {
        // Given
        var controllerType = typeof(BackofficeMessageQualityController);

        // When
        var route = controllerType.GetCustomAttribute<RouteAttribute>();
        var authorize = controllerType.GetCustomAttribute<AuthorizeAttribute>();
        var allowAnonymous = controllerType.GetCustomAttribute<AllowAnonymousAttribute>();

        // Then
        Assert.Equal("api/backoffice/message-warnings", route?.Template);
        Assert.Equal(ApiAuthorizationPolicies.ManagementAdmin, authorize?.Policy);
        Assert.Null(allowAnonymous);
    }
}
