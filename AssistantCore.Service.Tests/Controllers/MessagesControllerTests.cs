using System.Reflection;
using System.Text;
using AssistantCore.Service.Application.Commands.SendMessage;
using AssistantCore.Service.Application.Commands.SendMessage.Models;
using AssistantCore.Service.Application.Models.Conversations;
using AssistantCore.Service.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AssistantCore.Service.Tests.Controllers;

public sealed class MessagesControllerTests
{
    [Fact]
    public async Task Given_AValidRequest_When_SendMessage_Then_DispatchesCommandAndReturnsOk()
    {
        // Given
        var cancellationToken = new CancellationTokenSource().Token;
        var conversationId = Guid.NewGuid();
        var response = new SendMessageResponse(
            conversationId,
            Guid.NewGuid(),
            "Response",
            "gpt",
            [],
            [],
            DateTimeOffset.UtcNow);
        var dispatcher = new RecordingDispatcher { Response = response };
        var controller = new MessagesController(dispatcher);
        var request = new SendMessageRequest(conversationId, "Question");

        // When
        var actionResult = await controller.SendMessage(request, cancellationToken);

        // Then
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.Same(response, okResult.Value);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);
        var command = Assert.IsType<SendMessageCommand>(dispatcher.ReceivedRequest);
        Assert.Equal(conversationId, command.ConversationId);
        Assert.Equal("Question", command.Message);
        Assert.Equal(cancellationToken, dispatcher.ReceivedCancellationToken);
    }

    [Fact]
    public async Task Given_ANewConversation_When_SendMessageStream_Then_WritesTheSummaryInTheAcceptedEvent()
    {
        // Given
        var conversationId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 9, 1, 14, 30, 0, TimeSpan.Zero);
        var summary = new ConversationSummaryResponse(
            conversationId,
            "Politique de teletravail",
            "Active",
            1,
            createdAt,
            createdAt,
            LastMessagePreview: null);
        var controller = CreateStreamController(
            new SendMessageStreamEvent(
                SendMessageStreamEvent.Accepted,
                new
                {
                    ConversationId = conversationId,
                    UserMessageId = Guid.NewGuid(),
                    Conversation = summary
                }),
            out var body,
            out var httpContext);

        // When
        await controller.SendMessageStream(
            new SendMessageRequest(null, "Question"),
            CancellationToken.None);

        // Then
        var payload = Encoding.UTF8.GetString(body.ToArray());
        Assert.Equal("text/event-stream; charset=utf-8", httpContext.Response.ContentType);
        Assert.StartsWith(": connected", payload);
        Assert.Contains("event: message.accepted", payload);
        Assert.Contains(@"""conversation"":{", payload);
        Assert.Contains(@"""title"":""Politique de teletravail""", payload);
        Assert.Contains(@"""status"":""Active""", payload);
        Assert.Contains(@"""version"":1", payload);
        Assert.Contains(@"""lastMessagePreview"":null", payload);
    }

    [Fact]
    public async Task Given_AnyEvent_When_SendMessageStream_Then_UsesTheSameFieldCasingAsTheRestOfTheApi()
    {
        // Given
        var controller = CreateStreamController(
            new SendMessageStreamEvent(
                SendMessageStreamEvent.Accepted,
                new
                {
                    ConversationId = Guid.NewGuid(),
                    UserMessageId = Guid.NewGuid()
                }),
            out var body,
            out _);

        // When
        await controller.SendMessageStream(
            new SendMessageRequest(null, "Question"),
            CancellationToken.None);

        // Then
        var payload = Encoding.UTF8.GetString(body.ToArray());
        Assert.Contains(@"""conversationId""", payload);
        Assert.Contains(@"""userMessageId""", payload);
        Assert.DoesNotContain(@"""ConversationId""", payload);
        Assert.DoesNotContain(@"""UserMessageId""", payload);
    }

    [Fact]
    public void Given_TheSendMessageAction_When_SendMessage_Then_RequiresAuthorizationAndUsesExpectedRoute()
    {
        // Given
        var controllerType = typeof(MessagesController);

        // When
        var controllerRoute = controllerType.GetCustomAttribute<RouteAttribute>();
        var authorizeAttribute = controllerType.GetCustomAttribute<AuthorizeAttribute>();
        var method = controllerType.GetMethod(nameof(MessagesController.SendMessage));

        // Then
        Assert.NotNull(method);
        Assert.Equal("api/messages", controllerRoute?.Template);
        Assert.NotNull(authorizeAttribute);
        Assert.NotNull(method.GetCustomAttribute<HttpPostAttribute>());
    }

    private static MessagesController CreateStreamController(
        SendMessageStreamEvent streamEvent,
        out MemoryStream body,
        out DefaultHttpContext httpContext)
    {
        var dispatcher = new RecordingDispatcher
        {
            Response = ToAsyncEnumerable(streamEvent)
        };
        body = new MemoryStream();
        httpContext = new DefaultHttpContext();
        httpContext.Response.Body = body;

        return new MessagesController(dispatcher)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static async IAsyncEnumerable<SendMessageStreamEvent> ToAsyncEnumerable(
        SendMessageStreamEvent streamEvent)
    {
        await Task.CompletedTask;
        yield return streamEvent;
    }
}
