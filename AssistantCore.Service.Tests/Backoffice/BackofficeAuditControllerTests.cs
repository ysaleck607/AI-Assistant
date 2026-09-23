using System.Reflection;
using AssistantCore.Service.Application.Commands.GetBackofficeAudit;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Controllers;
using AssistantCore.Service.Infrastructure.Authentication.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AssistantCore.Service.Tests.Backoffice;

public sealed class BackofficeAuditControllerTests
{
    [Theory, AutoDomainData]
    public async Task Given_Filters_When_GetAuditEntries_Then_DispatchesTheQueryAndReturnsOk(
        Guid organizationId,
        Guid actorId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken,
        BackofficeAuditListResponse response)
    {
        // Given
        var dispatcher = new RecordingDispatcher { Response = response };
        var controller = new BackofficeAuditController(dispatcher);

        // When
        var actionResult = await controller.GetAuditEntries(
            page: 2,
            pageSize: 10,
            organizationId: organizationId,
            actor: actorId.ToString(),
            action: "MemberRoleChanged",
            result: "Succeeded",
            from: from,
            to: to,
            cancellationToken: cancellationToken);

        // Then
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.Same(response, okResult.Value);
        var query = Assert.IsType<GetBackofficeAuditQuery>(dispatcher.ReceivedRequest);
        Assert.Equal(2, query.Page);
        Assert.Equal(10, query.PageSize);
        Assert.Equal(organizationId, query.OrganizationId);
        Assert.Equal(actorId, query.ActorId);
        Assert.Equal("MemberRoleChanged", query.Action);
        Assert.Equal("Succeeded", query.Result);
        Assert.Equal(from, query.From);
        Assert.Equal(to, query.To);
        Assert.Equal(cancellationToken, dispatcher.ReceivedCancellationToken);
    }

    [Theory, AutoDomainData]
    public async Task Given_ANonGuidActor_When_GetAuditEntries_Then_ActorIdIsNull(
        BackofficeAuditListResponse response,
        CancellationToken cancellationToken)
    {
        // Given
        var dispatcher = new RecordingDispatcher { Response = response };
        var controller = new BackofficeAuditController(dispatcher);

        // When
        await controller.GetAuditEntries(actor: "not-a-guid", cancellationToken: cancellationToken);

        // Then
        var query = Assert.IsType<GetBackofficeAuditQuery>(dispatcher.ReceivedRequest);
        Assert.Null(query.ActorId);
    }

    [Fact]
    public void Given_Controller_When_InspectingAuthorization_Then_RequiresTheManagementAdminPolicy()
    {
        // Given
        var controllerType = typeof(BackofficeAuditController);

        // When
        var route = controllerType.GetCustomAttribute<RouteAttribute>();
        var authorize = controllerType.GetCustomAttribute<AuthorizeAttribute>();
        var allowAnonymous = controllerType.GetCustomAttribute<AllowAnonymousAttribute>();

        // Then
        Assert.Equal("api/backoffice/audit", route?.Template);
        Assert.Equal(ApiAuthorizationPolicies.ManagementAdmin, authorize?.Policy);
        Assert.Null(allowAnonymous);
    }
}
