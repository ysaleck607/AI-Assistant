using System.Reflection;
using AssistantCore.Service.Application.Commands.GetMicrosoft365ReindexStatus;
using AssistantCore.Service.Application.Commands.RequestMicrosoft365Reindex;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Controllers;
using AssistantCore.Service.Infrastructure.Authentication.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class BackofficeMicrosoft365ControllerTests
{
    [Theory, AutoDomainData]
    public async Task Given_AnOrganizationAndAReason_When_RequestReindex_Then_DispatchesTheCommandAndReturnsAccepted(
        Guid organizationId,
        CancellationToken cancellationToken,
        BackofficeMicrosoft365ReindexResponse response)
    {
        // Given
        var dispatcher = new RecordingDispatcher { Response = response };
        var controller = new BackofficeMicrosoft365Controller(dispatcher);

        // When
        var actionResult = await controller.RequestReindex(
            organizationId,
            new BackofficeMicrosoft365ReindexRequest("connector fixed"),
            cancellationToken);

        // Then
        var acceptedResult = Assert.IsType<AcceptedAtActionResult>(actionResult.Result);
        Assert.Same(response, acceptedResult.Value);
        var command = Assert.IsType<RequestMicrosoft365ReindexCommand>(dispatcher.ReceivedRequest);
        Assert.Equal(organizationId, command.OrganizationId);
        Assert.Equal("connector fixed", command.Reason);
        Assert.Equal(cancellationToken, dispatcher.ReceivedCancellationToken);
    }

    [Theory, AutoDomainData]
    public async Task Given_NoBody_When_RequestReindex_Then_DispatchesTheCommandWithoutReason(
        Guid organizationId,
        CancellationToken cancellationToken,
        BackofficeMicrosoft365ReindexResponse response)
    {
        // Given
        var dispatcher = new RecordingDispatcher { Response = response };
        var controller = new BackofficeMicrosoft365Controller(dispatcher);

        // When
        await controller.RequestReindex(organizationId, request: null, cancellationToken);

        // Then
        var command = Assert.IsType<RequestMicrosoft365ReindexCommand>(dispatcher.ReceivedRequest);
        Assert.Equal(organizationId, command.OrganizationId);
        Assert.Null(command.Reason);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnOperationIdentifier_When_GetReindexStatus_Then_DispatchesTheQueryAndReturnsOk(
        Guid organizationId,
        Guid operationId,
        CancellationToken cancellationToken,
        BackofficeMicrosoft365ReindexStatusResponse response)
    {
        // Given
        var dispatcher = new RecordingDispatcher { Response = response };
        var controller = new BackofficeMicrosoft365Controller(dispatcher);

        // When
        var actionResult = await controller.GetReindexStatus(organizationId, operationId, cancellationToken);

        // Then
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.Same(response, okResult.Value);
        var query = Assert.IsType<GetMicrosoft365ReindexStatusQuery>(dispatcher.ReceivedRequest);
        Assert.Equal(organizationId, query.OrganizationId);
        Assert.Equal(operationId, query.OperationId);
    }

    [Theory, AutoDomainData]
    public void Given_Controller_When_InspectingAuthorization_Then_RequiresTheSynaptixOperatorPolicy(int _)
    {
        // Given
        var controllerType = typeof(BackofficeMicrosoft365Controller);

        // When
        var route = controllerType.GetCustomAttribute<RouteAttribute>();
        var authorize = controllerType.GetCustomAttribute<AuthorizeAttribute>();
        var allowAnonymous = controllerType.GetCustomAttribute<AllowAnonymousAttribute>();
        var reindexMethod = controllerType.GetMethod(
            nameof(BackofficeMicrosoft365Controller.RequestReindex));
        var statusMethod = controllerType.GetMethod(
            nameof(BackofficeMicrosoft365Controller.GetReindexStatus));

        // Then
        Assert.Equal("api/backoffice/organizations/{organizationId:guid}/microsoft365", route?.Template);
        Assert.Equal(ApiAuthorizationPolicies.ManagementAdmin, authorize?.Policy);
        Assert.Null(allowAnonymous);
        Assert.Equal("reindex", reindexMethod?.GetCustomAttribute<HttpPostAttribute>()?.Template);
        Assert.Equal(
            "reindex/{operationId:guid}",
            statusMethod?.GetCustomAttribute<HttpGetAttribute>()?.Template);
    }
}
