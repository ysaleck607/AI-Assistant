using System.Reflection;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Service.Application.Commands.GetBackofficeIncidentDetails;
using AssistantCore.Service.Application.Commands.GetBackofficeIncidents;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Controllers;
using AssistantCore.Service.Infrastructure.Authentication.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AssistantCore.Service.Tests.Backoffice;

public sealed class BackofficeErrorsControllerTests
{
    [Theory, AutoDomainData]
    public async Task Given_Filters_When_GetIncidents_Then_DispatchesTheQueryAndReturnsOk(
        Guid organizationId,
        DateTimeOffset from,
        DateTimeOffset to,
        string correlationId,
        CancellationToken cancellationToken,
        BackofficeIncidentListResponse response)
    {
        // Given
        var dispatcher = new RecordingDispatcher { Response = response };
        var controller = new BackofficeErrorsController(dispatcher);

        // When
        var actionResult = await controller.GetIncidents(
            page: 2,
            pageSize: 10,
            organizationId: organizationId,
            from: from,
            to: to,
            subsystem: OperationalIncidentSubsystem.FoundryLlm,
            severity: OperationalIncidentSeverity.Error,
            correlationId: correlationId,
            cancellationToken: cancellationToken);

        // Then
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.Same(response, okResult.Value);
        var query = Assert.IsType<GetBackofficeIncidentsQuery>(dispatcher.ReceivedRequest);
        Assert.Equal(2, query.Page);
        Assert.Equal(10, query.PageSize);
        Assert.Equal(organizationId, query.OrganizationId);
        Assert.Equal(from, query.From);
        Assert.Equal(to, query.To);
        Assert.Equal(OperationalIncidentSubsystem.FoundryLlm, query.Subsystem);
        Assert.Equal(OperationalIncidentSeverity.Error, query.Severity);
        Assert.Equal(correlationId, query.CorrelationId);
        Assert.Equal(cancellationToken, dispatcher.ReceivedCancellationToken);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnIncidentId_When_GetIncidentDetails_Then_DispatchesTheQueryAndReturnsOk(
        Guid incidentId,
        CancellationToken cancellationToken,
        BackofficeIncidentDetailDto response)
    {
        // Given
        var dispatcher = new RecordingDispatcher { Response = response };
        var controller = new BackofficeErrorsController(dispatcher);

        // When
        var actionResult = await controller.GetIncidentDetails(incidentId, cancellationToken);

        // Then
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.Same(response, okResult.Value);
        var query = Assert.IsType<GetBackofficeIncidentDetailsQuery>(dispatcher.ReceivedRequest);
        Assert.Equal(incidentId, query.IncidentId);
    }

    [Fact]
    public void Given_Controller_When_InspectingAuthorization_Then_RequiresTheManagementAdminPolicy()
    {
        // Given
        var controllerType = typeof(BackofficeErrorsController);

        // When
        var route = controllerType.GetCustomAttribute<RouteAttribute>();
        var authorize = controllerType.GetCustomAttribute<AuthorizeAttribute>();
        var allowAnonymous = controllerType.GetCustomAttribute<AllowAnonymousAttribute>();

        // Then
        Assert.Equal("api/backoffice/errors", route?.Template);
        Assert.Equal(ApiAuthorizationPolicies.ManagementAdmin, authorize?.Policy);

        // Garde specifiquement contre le gap [AllowAnonymous] de BackofficeOrganizationsController.
        Assert.Null(allowAnonymous);
    }
}
