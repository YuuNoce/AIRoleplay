using System;
using System.Collections.Generic;
using System.Text.Json;

namespace AIRoleplay;

public static class LlmJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "...";
    }

    public static string BuildRedactedResponsePreview(string responseText, int maxLength)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            var redacted = RedactJsonElement(document.RootElement);
            var redactedJson = JsonSerializer.Serialize(redacted, Options);
            return Truncate(CleanForSingleLineLog(redactedJson), maxLength);
        }
        catch (JsonException)
        {
            return Truncate(CleanForSingleLineLog(responseText), maxLength);
        }
    }

    private static object? RedactJsonElement(JsonElement element, string? propertyName = null)
    {
        if (propertyName is "content" or "reasoning_content" or "text")
        {
            var value = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
            return $"(redacted length={value?.Length ?? 0})";
        }

        return element.ValueKind switch
        {
            JsonValueKind.Object => RedactJsonObject(element),
            JsonValueKind.Array => RedactJsonArray(element),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText(),
        };
    }

    private static Dictionary<string, object?> RedactJsonObject(JsonElement element)
    {
        var values = new Dictionary<string, object?>();
        foreach (var property in element.EnumerateObject())
        {
            values[property.Name] = RedactJsonElement(property.Value, property.Name);
        }

        return values;
    }

    private static List<object?> RedactJsonArray(JsonElement element)
    {
        var values = new List<object?>();
        foreach (var item in element.EnumerateArray())
        {
            values.Add(RedactJsonElement(item));
        }

        return values;
    }

    private static string CleanForSingleLineLog(string value) =>
        value.Replace("\r", "\\r").Replace("\n", "\\n");
}
