using System.Text.Json;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Messages.Tools;
using AssistantCore.Service.Application.Services.Messages.Tools;

namespace AssistantCore.Service.Tests.Messages;

public sealed class AiToolCallValidatorTests
{
    [Theory, AutoDomainData]
    public async Task Given_AValidRegisteredTool_When_ValidateAsync_Then_ReturnsValidatedCall(
        Guid callId)
    {
        // Given
        var requestedToolCall = CreateMicrosoft365Call(callId.ToString());
        var validator = CreateValidator();

        // When
        var result = await validator.ValidateAsync(
            requestedToolCall,
            [CreateMicrosoft365Tool()],
            CancellationToken.None);

        // Then
        Assert.Equal(requestedToolCall.CallId, result.CallId);
        Assert.Equal(AiToolNames.SearchMicrosoft365, result.ToolName);
        Assert.Equal(
            requestedToolCall.Arguments.GetRawText(),
            result.Arguments.GetRawText());
    }

    [Theory, AutoDomainData]
    public async Task Given_AnUnknownTool_When_ValidateAsync_Then_RejectsCall(Guid callId)
    {
        // Given
        var requestedToolCall = CreateMicrosoft365Call(
            callId.ToString(),
            toolName: "unknown_tool");
        var validator = CreateValidator();

        // When
        var exception = await Assert.ThrowsAsync<ToolCallValidationException>(() =>
            validator.ValidateAsync(
                requestedToolCall,
                [CreateMicrosoft365Tool()],
                CancellationToken.None));

        // Then
        Assert.Equal(callId.ToString(), exception.ToolCallId);
        Assert.Equal("TOOL_CALL_REJECTED", ToolCallValidationException.TechnicalCode);
    }

    [Theory, AutoDomainData]
    public async Task Given_ARegisteredWriteTool_When_ValidateAsync_Then_RejectsCall(Guid callId)
    {
        // Given
        const string writeToolName = "delete_erp_invoice";
        var requestedToolCall = CreateMicrosoft365Call(
            callId.ToString(),
            toolName: writeToolName);
        var availableTool = CreateMicrosoft365Tool(writeToolName);
        var validator = CreateValidator();

        // When
        var exception = await Assert.ThrowsAsync<ToolCallValidationException>(() =>
            validator.ValidateAsync(
                requestedToolCall,
                [availableTool],
                CancellationToken.None));

        // Then
        Assert.Contains("read-only", exception.Message, StringComparison.Ordinal);
    }

    [Theory, AutoDomainData]
    public async Task Given_AMissingRequiredField_When_ValidateAsync_Then_RejectsCall(Guid callId)
    {
        // Given
        var requestedToolCall = CreateMicrosoft365Call(
            callId.ToString(),
            new Dictionary<string, object?>
            {
                ["sourceTypes"] = new[] { "sharepoint" },
                ["dateFrom"] = null,
                ["dateTo"] = null
            });
        var validator = CreateValidator();

        // When
        var exception = await Assert.ThrowsAsync<ToolCallValidationException>(() =>
            validator.ValidateAsync(
                requestedToolCall,
                [CreateMicrosoft365Tool()],
                CancellationToken.None));

        // Then
        Assert.Contains("query", exception.Message, StringComparison.Ordinal);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnUnexpectedField_When_ValidateAsync_Then_RejectsCall(Guid callId)
    {
        // Given
        var arguments = CreateValidMicrosoft365Arguments();
        arguments["customField"] = "value";
        var requestedToolCall = CreateMicrosoft365Call(callId.ToString(), arguments);
        var validator = CreateValidator();

        // When
        var exception = await Assert.ThrowsAsync<ToolCallValidationException>(() =>
            validator.ValidateAsync(
                requestedToolCall,
                [CreateMicrosoft365Tool()],
                CancellationToken.None));

        // Then
        Assert.Contains("Unexpected field", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineAutoDomainData("url")]
    [InlineAutoDomainData("providerUrl")]
    [InlineAutoDomainData("endpoint")]
    [InlineAutoDomainData("sql")]
    [InlineAutoDomainData("table")]
    [InlineAutoDomainData("tableName")]
    [InlineAutoDomainData("organizationId")]
    [InlineAutoDomainData("tenantId")]
    [InlineAutoDomainData("token")]
    [InlineAutoDomainData("accessToken")]
    [InlineAutoDomainData("apiKey")]
    [InlineAutoDomainData("indexName")]
    [InlineAutoDomainData("odataFilter")]
    public async Task Given_ATechnicalField_When_ValidateAsync_Then_RejectsCall(
        string technicalFieldName,
        Guid callId)
    {
        // Given
        var arguments = CreateValidMicrosoft365Arguments();
        arguments[technicalFieldName] = "untrusted-value";
        var requestedToolCall = CreateMicrosoft365Call(callId.ToString(), arguments);
        var validator = CreateValidator();

        // When
        var exception = await Assert.ThrowsAsync<ToolCallValidationException>(() =>
            validator.ValidateAsync(
                requestedToolCall,
                [CreateMicrosoft365Tool()],
                CancellationToken.None));

        // Then
        Assert.Contains("Technical field", exception.Message, StringComparison.Ordinal);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnInvalidEnumValue_When_ValidateAsync_Then_RejectsCall(Guid callId)
    {
        // Given
        var arguments = CreateValidMicrosoft365Arguments();
        arguments["sourceTypes"] = new[] { "outlook" };
        var requestedToolCall = CreateMicrosoft365Call(callId.ToString(), arguments);
        var validator = CreateValidator();

        // When
        var exception = await Assert.ThrowsAsync<ToolCallValidationException>(() =>
            validator.ValidateAsync(
                requestedToolCall,
                [CreateMicrosoft365Tool()],
                CancellationToken.None));

        // Then
        Assert.Contains("not allowed", exception.Message, StringComparison.Ordinal);
    }

    [Theory, InlineAutoDomainData("SharePoint")]
    public async Task Given_ACaseVariantEnumValue_When_ValidateAsync_Then_AcceptsCall(
        string sourceType,
        Guid callId)
    {
        // Given
        var arguments = CreateValidMicrosoft365Arguments();
        arguments["sourceTypes"] = new[] { sourceType };
        var requestedToolCall = CreateMicrosoft365Call(callId.ToString(), arguments);
        var validator = CreateValidator();

        // When
        var result = await validator.ValidateAsync(
            requestedToolCall,
            [CreateMicrosoft365Tool()],
            CancellationToken.None);

        // Then
        Assert.Equal(callId.ToString(), result.CallId);
    }

    [Theory]
    [InlineAutoDomainData("2026-02-30", "2026-03-01")]
    [InlineAutoDomainData("2026-06-30", "2026-04-01")]
    public async Task Given_AnInvalidDateRange_When_ValidateAsync_Then_RejectsCall(
        string dateFrom,
        string dateTo,
        Guid callId)
    {
        // Given
        var arguments = CreateValidMicrosoft365Arguments();
        arguments["dateFrom"] = dateFrom;
        arguments["dateTo"] = dateTo;
        var requestedToolCall = CreateMicrosoft365Call(callId.ToString(), arguments);
        var validator = CreateValidator();

        // When
        var exception = await Assert.ThrowsAsync<ToolCallValidationException>(() =>
            validator.ValidateAsync(
                requestedToolCall,
                [CreateMicrosoft365Tool()],
                CancellationToken.None));

        // Then
        Assert.Equal(callId.ToString(), exception.ToolCallId);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnOversizedString_When_ValidateAsync_Then_RejectsCall(Guid callId)
    {
        // Given
        var arguments = CreateValidMicrosoft365Arguments();
        arguments["query"] = new string('a', 501);
        var requestedToolCall = CreateMicrosoft365Call(callId.ToString(), arguments);
        var validator = CreateValidator();

        // When
        var exception = await Assert.ThrowsAsync<ToolCallValidationException>(() =>
            validator.ValidateAsync(
                requestedToolCall,
                [CreateMicrosoft365Tool()],
                CancellationToken.None));

        // Then
        Assert.Contains("500", exception.Message, StringComparison.Ordinal);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnInvalidList_When_ValidateAsync_Then_RejectsCall(Guid callId)
    {
        // Given
        var arguments = CreateValidMicrosoft365Arguments();
        arguments["sourceTypes"] = new[] { "sharepoint", "sharepoint" };
        var requestedToolCall = CreateMicrosoft365Call(callId.ToString(), arguments);
        var validator = CreateValidator();

        // When
        var exception = await Assert.ThrowsAsync<ToolCallValidationException>(() =>
            validator.ValidateAsync(
                requestedToolCall,
                [CreateMicrosoft365Tool()],
                CancellationToken.None));

        // Then
        Assert.Contains("duplicate", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineAutoDomainData("-1")]
    [InlineAutoDomainData("101")]
    [InlineAutoDomainData("\"invalid\"")]
    public async Task Given_AnInvalidNumber_When_ValidateAsync_Then_RejectsCall(
        string amountJson,
        Guid callId)
    {
        // Given
        var schema = ParseJson(
            """
            {
              "type": "object",
              "properties": {
                "amount": { "type": "number", "minimum": 0, "maximum": 100 }
              },
              "required": ["amount"],
              "additionalProperties": false
            }
            """);
        var arguments = ParseJson($$"""{"amount":{{amountJson}}}""");
        var requestedToolCall = new AiRequestedToolCall(
            callId.ToString(),
            AiToolNames.SearchMicrosoft365,
            arguments);
        var availableTool = new AiToolDefinition(
            AiToolNames.SearchMicrosoft365,
            "Test numeric validation.",
            schema);
        var validator = CreateValidator();

        // When
        var exception = await Assert.ThrowsAsync<ToolCallValidationException>(() =>
            validator.ValidateAsync(
                requestedToolCall,
                [availableTool],
                CancellationToken.None));

        // Then
        Assert.Equal(callId.ToString(), exception.ToolCallId);
    }

    [Theory, AutoDomainData]
    public async Task Given_ACancelledRequest_When_ValidateAsync_Then_PropagatesCancellation(Guid callId)
    {
        // Given
        var requestedToolCall = CreateMicrosoft365Call(callId.ToString());
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();
        var validator = CreateValidator();

        // When
        var exception = await Record.ExceptionAsync(() =>
            validator.ValidateAsync(
                requestedToolCall,
                [CreateMicrosoft365Tool()],
                cancellationSource.Token));

        // Then
        Assert.IsType<OperationCanceledException>(exception);
    }

    private static AiRequestedToolCall CreateMicrosoft365Call(
        string callId,
        IReadOnlyDictionary<string, object?>? arguments = null,
        string toolName = AiToolNames.SearchMicrosoft365) => new(
            callId,
            toolName,
            JsonSerializer.SerializeToElement(
                arguments ?? CreateValidMicrosoft365Arguments()));

    private static AiToolCallValidator CreateValidator() => new(
        new AiToolArgumentSchemaValidator(),
        new AiToolArgumentSecurityValidator(),
        new AiToolDateRangeValidator());

    private static Dictionary<string, object?> CreateValidMicrosoft365Arguments() => new()
    {
        ["query"] = "rapport ventes trimestre",
        ["sourceTypes"] = new[] { "sharepoint" },
        ["dateFrom"] = "2026-04-01",
        ["dateTo"] = "2026-06-30"
    };

    private static AiToolDefinition CreateMicrosoft365Tool(
        string toolName = AiToolNames.SearchMicrosoft365) => new(
            toolName,
            "Search Microsoft 365.",
            ParseJson(
                """
                {
                  "type": "object",
                  "properties": {
                    "query": { "type": "string" },
                    "sourceTypes": {
                      "anyOf": [
                        {
                          "type": "array",
                          "items": {
                            "type": "string",
                            "enum": ["sharepoint", "onedrive"]
                          },
                          "uniqueItems": true
                        },
                        { "type": "null" }
                      ]
                    },
                    "dateFrom": {
                      "anyOf": [{ "type": "string" }, { "type": "null" }]
                    },
                    "dateTo": {
                      "anyOf": [{ "type": "string" }, { "type": "null" }]
                    }
                  },
                  "required": ["query", "sourceTypes", "dateFrom", "dateTo"],
                  "additionalProperties": false
                }
                """));

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
