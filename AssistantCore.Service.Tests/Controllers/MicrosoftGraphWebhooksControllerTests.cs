using AssistantCore.Service.Application.Commands.ReceiveMicrosoftGraphWebhook;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AssistantCore.Service.Tests.Controllers;

public sealed class MicrosoftGraphWebhooksControllerTests
{
    [Theory, AutoDomainData]
    public async Task Given_AValidationToken_When_ReceiveAsync_Then_ReturnsExactPlainText(
        string validationToken)
    {
        // Given
        var dispatcher = new RecordingDispatcher
        {
            Response = new ReceiveMicrosoftGraphWebhookResult(validationToken)
        };
        var controller = new MicrosoftGraphWebhooksController(dispatcher);

        // When
        var result = await controller.ReceiveAsync(
            validationToken,
            CancellationToken.None);

        // Then
        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(200, content.StatusCode ?? 200);
        Assert.Equal("text/plain; charset=utf-8", content.ContentType);
        Assert.Equal(validationToken, content.Content);
        var command = Assert.IsType<ReceiveMicrosoftGraphWebhookCommand>(dispatcher.ReceivedRequest);
        Assert.Equal(validationToken, command.ValidationToken);
    }

    [Theory, AutoDomainData]
    public async Task Given_NormalNotification_When_ReceiveAsync_Then_ReturnsAccepted(
        MicrosoftGraphNotification notification)
    {
        // Given
        var notifications = new MicrosoftGraphNotificationCollection([notification]);
        var dispatcher = new RecordingDispatcher
        {
            Response = new ReceiveMicrosoftGraphWebhookResult(ValidationToken: null)
        };
        var controller = CreateControllerWithJsonBody(dispatcher, notifications);

        // When
        var result = await controller.ReceiveAsync(
            validationToken: null,
            cancellationToken: CancellationToken.None);

        // Then
        Assert.IsType<AcceptedResult>(result);
        var command = Assert.IsType<ReceiveMicrosoftGraphWebhookCommand>(dispatcher.ReceivedRequest);
        Assert.NotNull(command.Notifications);
        var receivedNotification = Assert.Single(command.Notifications.Value!);
        Assert.Equal(notification, receivedNotification);
    }

    [Fact]
    public async Task Given_TooLongValidationToken_When_ReceiveAsync_Then_ReturnsBadRequest()
    {
        // Given
        var dispatcher = CreateUnusedDispatcher();
        var controller = new MicrosoftGraphWebhooksController(dispatcher);
        var validationToken = new string('a', 4097);

        // When
        var result = await controller.ReceiveAsync(validationToken, CancellationToken.None);

        // Then
        Assert.IsType<BadRequestResult>(result);
        Assert.Null(dispatcher.ReceivedRequest);
    }

    [Fact]
    public async Task Given_NonJsonNotificationRequest_When_ReceiveAsync_Then_ReturnsUnsupportedMediaType()
    {
        // Given
        var dispatcher = CreateUnusedDispatcher();
        var controller = new MicrosoftGraphWebhooksController(dispatcher);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentType = "text/plain";
        httpContext.Request.Body = new MemoryStream("{}"u8.ToArray());
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // When
        var result = await controller.ReceiveAsync(null, CancellationToken.None);

        // Then
        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, status.StatusCode);
        Assert.Null(dispatcher.ReceivedRequest);
    }

    [Fact]
    public async Task Given_EmptyNotificationCollection_When_ReceiveAsync_Then_ReturnsBadRequest()
    {
        // Given
        var dispatcher = CreateUnusedDispatcher();
        var controller = CreateControllerWithJsonBody(
            dispatcher,
            new MicrosoftGraphNotificationCollection([]));

        // When
        var result = await controller.ReceiveAsync(null, CancellationToken.None);

        // Then
        Assert.IsType<BadRequestResult>(result);
        Assert.Null(dispatcher.ReceivedRequest);
    }

    [Theory, AutoDomainData]
    public async Task Given_MoreThanMaximumNotifications_When_ReceiveAsync_Then_ReturnsPayloadTooLarge(
        MicrosoftGraphNotification notification)
    {
        // Given
        var dispatcher = CreateUnusedDispatcher();
        var notifications = Enumerable.Repeat(notification, 101).ToArray();
        var controller = CreateControllerWithJsonBody(
            dispatcher,
            new MicrosoftGraphNotificationCollection(notifications));

        // When
        var result = await controller.ReceiveAsync(null, CancellationToken.None);

        // Then
        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, status.StatusCode);
        Assert.Null(dispatcher.ReceivedRequest);
    }

    private static RecordingDispatcher CreateUnusedDispatcher() =>
        new()
        {
            Response = new ReceiveMicrosoftGraphWebhookResult(ValidationToken: null)
        };

    private static MicrosoftGraphWebhooksController CreateControllerWithJsonBody(
        RecordingDispatcher dispatcher,
        MicrosoftGraphNotificationCollection notifications)
    {
        var controller = new MicrosoftGraphWebhooksController(dispatcher);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.Body = new MemoryStream(
            JsonSerializer.SerializeToUtf8Bytes(notifications));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }
}
