using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentRuntime.LLM;

/// <summary>
/// Repairs the common ways models misshape tool-call arguments, guided by the tool's JSON schema:
/// a list or object sent as a JSON-encoded string (<c>"changes": "[{...}]"</c>), the whole
/// arguments double-encoded, one item where a list belongs, and numbers or booleans in quotes.
/// <para>It only ever changes a value whose JSON type the schema doesn't allow there, so arguments
/// that were already valid come back unchanged.</para>
/// </summary>
public static class ToolArguments
{
    private const int MaxDepth = 64;

    /// <summary>The arguments, repaired to fit <paramref name="jsonSchema"/> where they can be.
    /// Returns the input as it is when it isn't JSON or the schema can't be read.</summary>
    public static string Repair(string argumentsJson, string jsonSchema)
    {
        JsonNode? schema;
        JsonNode? value;
        try
        {
            schema = JsonNode.Parse(jsonSchema);
            value = JsonNode.Parse(argumentsJson);
        }
        catch (JsonException)
        {
            return argumentsJson;
        }

        var state = new State();
        var repaired = Fix(value, schema as JsonObject, 0, state);
        return state.Changed ? repaired?.ToJsonString() ?? argumentsJson : argumentsJson;
    }

    /// <summary>
    /// The response with each tool call's arguments repaired against the schema of the tool it
    /// names in <paramref name="request"/>. The same response when nothing needed it, so a valid
    /// answer passes through untouched. Every model call goes through this (the LLM router), so
    /// agents' tools and the design assistants all get it.
    /// </summary>
    public static LlmCompletionResponse Repair(LlmCompletionRequest request, LlmCompletionResponse response)
    {
        if (response.ToolCalls is not { Count: > 0 } calls || request.Tools.Count == 0) return response;
        List<ToolCall>? repaired = null;
        for (var i = 0; i < calls.Count; i++)
        {
            var tool = request.Tools.FirstOrDefault(t => t.Name == calls[i].Name);
            if (tool is null || string.IsNullOrWhiteSpace(tool.JsonSchema)) continue;
            var arguments = Repair(calls[i].ArgumentsJson, tool.JsonSchema);
            if (ReferenceEquals(arguments, calls[i].ArgumentsJson)) continue;
            repaired ??= [.. calls];
            repaired[i] = calls[i] with { ArgumentsJson = arguments };
        }
        return repaired is null ? response : response with { ToolCalls = repaired };
    }

    /// <summary>The value, repaired to fit the schema. May return the same node, changed in place.</summary>
    public static JsonNode? Repair(JsonNode? value, JsonNode? schema) => Fix(value, schema as JsonObject, 0, new State());

    private sealed class State
    {
        public bool Changed;
    }

    private static JsonNode? Fix(JsonNode? value, JsonObject? schema, int depth, State state)
    {
        if (schema is null || value is null || depth > MaxDepth) return value;
        var types = Types(schema);

        if (types.Count > 0 && !types.Contains(KindOf(value)) && !(KindOf(value) == "integer" && types.Contains("number")))
        {
            var fixedValue = Coerce(value, types, schema);
            if (!ReferenceEquals(fixedValue, value))
            {
                state.Changed = true;
                value = fixedValue;
            }
        }

        switch (value)
        {
            case JsonObject obj when schema["properties"] is JsonObject properties:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    var propertySchema = properties.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Value as JsonObject;
                    if (propertySchema is null) continue;
                    var child = obj[key];
                    var fixedChild = Fix(child, propertySchema, depth + 1, state);
                    if (!ReferenceEquals(fixedChild, child)) obj[key] = Detach(fixedChild);
                }
                break;
            case JsonArray list when schema["items"] is JsonObject items:
                for (var i = 0; i < list.Count; i++)
                {
                    var child = list[i];
                    var fixedChild = Fix(child, items, depth + 1, state);
                    if (!ReferenceEquals(fixedChild, child)) list[i] = Detach(fixedChild);
                }
                break;
        }

        return value;
    }

    /// <summary>The value as one of the allowed types, or the value itself when it can't be.</summary>
    private static JsonNode? Coerce(JsonNode value, IReadOnlySet<string> types, JsonObject schema)
    {
        // A string holding JSON: decode it (up to a few layers) and use it if it fits.
        if (value is JsonValue v && v.TryGetValue<string>(out var text))
        {
            var decoded = Decode(text);
            if (decoded is not null && (types.Contains(KindOf(decoded)) || types.Contains("array") && decoded is not JsonArray))
            {
                return types.Contains(KindOf(decoded)) ? decoded : Wrap(decoded, schema);
            }

            var trimmed = text.Trim();
            if ((types.Contains("integer") || types.Contains("number")) &&
                double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                if (types.Contains("integer") && Math.Abs(number % 1) < double.Epsilon && Math.Abs(number) < long.MaxValue) return JsonValue.Create((long)number);
                if (types.Contains("number")) return JsonValue.Create(number);
            }

            if (types.Contains("boolean") && trimmed.ToLowerInvariant() is "true" or "false" or "yes" or "no")
            {
                return JsonValue.Create(trimmed.ToLowerInvariant() is "true" or "yes");
            }
        }

        // One item where a list belongs.
        if (types.Contains("array") && value is not JsonArray) return Wrap(value, schema);
        return value;
    }

    private static JsonNode? Wrap(JsonNode item, JsonObject schema)
    {
        // Only when the item is what the list holds (or the list doesn't say).
        if (schema["items"] is JsonObject items && Types(items) is { Count: > 0 } itemTypes && !itemTypes.Contains(KindOf(item))) return item;
        return new JsonArray(Detach(item));
    }

    private static JsonNode? Decode(string text)
    {
        JsonNode? node = null;
        var current = text;
        for (var layer = 0; layer < 3; layer++)
        {
            var trimmed = current.Trim();
            if (trimmed.Length == 0 || trimmed[0] is not ('[' or '{' or '"')) break;
            try
            {
                node = JsonNode.Parse(trimmed);
            }
            catch (JsonException)
            {
                return null;
            }

            if (node is JsonValue inner && inner.TryGetValue<string>(out var again)) current = again;
            else break;
        }
        return node is JsonObject or JsonArray ? node : null;
    }

    private static HashSet<string> Types(JsonObject schema)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        switch (schema["type"])
        {
            case JsonValue t when t.TryGetValue<string>(out var one):
                set.Add(one);
                break;
            case JsonArray many:
                foreach (var t in many) if (t is JsonValue tv && tv.TryGetValue<string>(out var s)) set.Add(s);
                break;
        }
        // An enum or a const with no type: leave the value to the reader.
        return set;
    }

    private static string KindOf(JsonNode node) => node.GetValueKind() switch
    {
        JsonValueKind.Object => "object",
        JsonValueKind.Array => "array",
        JsonValueKind.String => "string",
        JsonValueKind.Number => node.AsValue().TryGetValue<long>(out _) ? "integer" : "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        _ => "null"
    };

    /// <summary>A node that can be put under a new parent.</summary>
    private static JsonNode? Detach(JsonNode? node) => node?.Parent is null ? node : node.DeepClone();
}
