using System.Text.Json;
using AssistantCore.Service.Application.Exceptions;

namespace AssistantCore.Service.Application.Services.Messages.Tools;

public sealed class AiToolArgumentSchemaValidator : IAiToolArgumentSchemaValidator
{
    private const int MaximumStringLength = 500;
    private const int MaximumArrayLength = 20;

    public void Validate(JsonElement arguments, JsonElement schema, string toolCallId)
    {
        ValidateValue(arguments, schema, "arguments", toolCallId);
    }

    private static void ValidateValue(
        JsonElement value,
        JsonElement schema,
        string path,
        string toolCallId)
    {
        if (schema.TryGetProperty("anyOf", out var alternatives))
        {
            var matchingAlternatives = alternatives
                .EnumerateArray()
                .Where(alternative => MatchesValueKind(value, alternative))
                .ToArray();

            if (matchingAlternatives.Length == 1)
            {
                ValidateValue(value, matchingAlternatives[0], path, toolCallId);
                return;
            }

            if (!matchingAlternatives.Any(alternative =>
                    IsValidAlternative(value, alternative, path, toolCallId)))
            {
                throw Reject(toolCallId, $"Field '{path}' does not match an accepted type.");
            }

            return;
        }

        if (!schema.TryGetProperty("type", out var schemaTypeElement)
            || schemaTypeElement.ValueKind != JsonValueKind.String)
        {
            throw Reject(toolCallId, $"Field '{path}' has an invalid backend schema.");
        }

        switch (schemaTypeElement.GetString())
        {
            case "object":
                ValidateObject(value, schema, path, toolCallId);
                break;
            case "array":
                ValidateArray(value, schema, path, toolCallId);
                break;
            case "string":
                ValidateString(value, schema, path, toolCallId);
                break;
            case "number":
                ValidateNumber(value, schema, path, toolCallId, integerOnly: false);
                break;
            case "integer":
                ValidateNumber(value, schema, path, toolCallId, integerOnly: true);
                break;
            case "boolean":
                EnsureValueKind(
                    value,
                    JsonValueKind.True,
                    JsonValueKind.False,
                    path,
                    toolCallId);
                break;
            case "null":
                EnsureValueKind(value, JsonValueKind.Null, path, toolCallId);
                break;
            default:
                throw Reject(
                    toolCallId,
                    $"Field '{path}' uses an unsupported backend schema type.");
        }
    }

    private static bool MatchesValueKind(JsonElement value, JsonElement schema)
    {
        if (schema.TryGetProperty("anyOf", out var alternatives))
        {
            return alternatives.EnumerateArray().Any(alternative =>
                MatchesValueKind(value, alternative));
        }

        if (!schema.TryGetProperty("type", out var schemaTypeElement))
        {
            return false;
        }

        return schemaTypeElement.GetString() switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "number" or "integer" => value.ValueKind == JsonValueKind.Number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => false
        };
    }

    private static bool IsValidAlternative(
        JsonElement value,
        JsonElement schema,
        string path,
        string toolCallId)
    {
        try
        {
            ValidateValue(value, schema, path, toolCallId);
            return true;
        }
        catch (ToolCallValidationException)
        {
            return false;
        }
    }

    private static void ValidateObject(
        JsonElement value,
        JsonElement schema,
        string path,
        string toolCallId)
    {
        EnsureValueKind(value, JsonValueKind.Object, path, toolCallId);

        if (!schema.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object)
        {
            throw Reject(toolCallId, $"Field '{path}' has an invalid object schema.");
        }

        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var requiredProperty in required.EnumerateArray())
            {
                var propertyName = requiredProperty.GetString();
                if (string.IsNullOrWhiteSpace(propertyName)
                    || !value.TryGetProperty(propertyName, out _))
                {
                    throw Reject(toolCallId, $"Required field '{propertyName}' is missing.");
                }
            }
        }

        foreach (var property in value.EnumerateObject())
        {
            if (!properties.TryGetProperty(property.Name, out var propertySchema))
            {
                throw Reject(toolCallId, $"Unexpected field '{property.Name}' is not allowed.");
            }

            ValidateValue(
                property.Value,
                propertySchema,
                $"{path}.{property.Name}",
                toolCallId);
        }
    }

    private static void ValidateArray(
        JsonElement value,
        JsonElement schema,
        string path,
        string toolCallId)
    {
        EnsureValueKind(value, JsonValueKind.Array, path, toolCallId);

        var items = value.EnumerateArray().ToArray();
        if (items.Length is 0 or > MaximumArrayLength)
        {
            throw Reject(
                toolCallId,
                $"Field '{path}' must contain between 1 and {MaximumArrayLength} values.");
        }

        if (!schema.TryGetProperty("items", out var itemSchema))
        {
            throw Reject(toolCallId, $"Field '{path}' has an invalid array schema.");
        }

        var distinctItems = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!distinctItems.Add(item.GetRawText()))
            {
                throw Reject(toolCallId, $"Field '{path}' cannot contain duplicate values.");
            }

            ValidateValue(item, itemSchema, $"{path}[]", toolCallId);
        }
    }

    private static void ValidateString(
        JsonElement value,
        JsonElement schema,
        string path,
        string toolCallId)
    {
        EnsureValueKind(value, JsonValueKind.String, path, toolCallId);

        var stringValue = value.GetString();
        if (string.IsNullOrWhiteSpace(stringValue) || stringValue.Length > MaximumStringLength)
        {
            throw Reject(
                toolCallId,
                $"Field '{path}' must contain between 1 and {MaximumStringLength} characters.");
        }

        if (schema.TryGetProperty("enum", out var allowedValues)
            && !allowedValues.EnumerateArray().Any(allowedValue =>
                allowedValue.ValueKind == JsonValueKind.String
                && string.Equals(
                    allowedValue.GetString(),
                    stringValue,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw Reject(toolCallId, $"Field '{path}' contains a value that is not allowed.");
        }
    }

    private static void ValidateNumber(
        JsonElement value,
        JsonElement schema,
        string path,
        string toolCallId,
        bool integerOnly)
    {
        EnsureValueKind(value, JsonValueKind.Number, path, toolCallId);

        if (!value.TryGetDecimal(out var number)
            || integerOnly && decimal.Truncate(number) != number)
        {
            throw Reject(
                toolCallId,
                $"Field '{path}' must contain a valid {(integerOnly ? "integer" : "number")}.");
        }

        if (schema.TryGetProperty("minimum", out var minimum)
            && number < minimum.GetDecimal())
        {
            throw Reject(toolCallId, $"Field '{path}' is below its minimum value.");
        }

        if (schema.TryGetProperty("maximum", out var maximum)
            && number > maximum.GetDecimal())
        {
            throw Reject(toolCallId, $"Field '{path}' exceeds its maximum value.");
        }
    }

    private static void EnsureValueKind(
        JsonElement value,
        JsonValueKind expectedKind,
        string path,
        string toolCallId)
    {
        if (value.ValueKind != expectedKind)
        {
            throw Reject(toolCallId, $"Field '{path}' has an invalid type.");
        }
    }

    private static void EnsureValueKind(
        JsonElement value,
        JsonValueKind firstExpectedKind,
        JsonValueKind secondExpectedKind,
        string path,
        string toolCallId)
    {
        if (value.ValueKind != firstExpectedKind && value.ValueKind != secondExpectedKind)
        {
            throw Reject(toolCallId, $"Field '{path}' has an invalid type.");
        }
    }

    private static ToolCallValidationException Reject(string toolCallId, string message) =>
        new(toolCallId, message);
}
