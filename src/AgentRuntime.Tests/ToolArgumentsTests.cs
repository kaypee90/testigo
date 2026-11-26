using System.Text.Json;
using System.Text.Json.Nodes;
using AgentRuntime.LLM;

namespace AgentRuntime.Tests;

/// <summary>
/// Repairing misshaped tool-call arguments against the tool's schema: what models get wrong is
/// fixed, and arguments that were already right are never touched.
/// </summary>
public sealed class ToolArgumentsTests
{
    private const string Schema = """
    {
      "type": "object",
      "properties": {
        "reply": { "type": "string" },
        "count": { "type": "integer" },
        "ratio": { "type": ["number", "null"] },
        "ok": { "type": "boolean" },
        "tags": { "type": "array", "items": { "type": "string" } },
        "changes": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": { "op": { "type": "string" }, "value": { "type": "object" } }
          }
        }
      }
    }
    """;

    [Theory]
    [InlineData("""{"reply":"Hi","count":3,"ratio":null,"ok":true,"tags":["a"],"changes":[{"op":"set","value":{"x":1}}]}""")]
    // A string that merely looks like JSON stays a string where a string belongs.
    [InlineData("""{"reply":"[{\"not\":\"a list\"}]","changes":[]}""")]
    [InlineData("""{"reply":"Café <b>&</b>"}""")]
    public void Arguments_that_fit_the_schema_come_back_exactly_as_they_were(string json)
    {
        Assert.Same(json, ToolArguments.Repair(json, Schema));
    }

    [Fact]
    public void A_list_sent_as_a_json_string_is_decoded()
    {
        var json = JsonSerializer.Serialize(new { reply = "Done", changes = JsonSerializer.Serialize(new[] { new { op = "set", value = new { x = 1 } } }) });

        var repaired = JsonNode.Parse(ToolArguments.Repair(json, Schema))!;

        Assert.Equal("set", repaired["changes"]![0]!["op"]!.GetValue<string>());
        Assert.Equal(1, repaired["changes"]![0]!["value"]!["x"]!.GetValue<int>());
    }

    [Fact]
    public void Nested_strings_numbers_booleans_and_single_items_are_put_right()
    {
        var json = """{"count":"4","ratio":"0.5","ok":"true","tags":"solo","changes":{"op":"set","value":"{\"x\":2}"}}""";

        var repaired = JsonNode.Parse(ToolArguments.Repair(json, Schema))!;

        Assert.Equal(4, repaired["count"]!.GetValue<long>());
        Assert.Equal(0.5, repaired["ratio"]!.GetValue<double>());
        Assert.True(repaired["ok"]!.GetValue<bool>());
        Assert.Equal("solo", Assert.Single(repaired["tags"]!.AsArray())!.GetValue<string>());
        Assert.Equal(2, repaired["changes"]![0]!["value"]!["x"]!.GetValue<int>());
    }

    [Fact]
    public void Double_encoded_arguments_are_decoded()
    {
        var inner = """{"reply":"Hi","changes":[]}""";
        var repaired = JsonNode.Parse(ToolArguments.Repair(JsonSerializer.Serialize(inner), Schema))!;
        Assert.Equal("Hi", repaired["reply"]!.GetValue<string>());
    }

    [Fact]
    public void What_cant_be_repaired_is_left_for_the_reader_to_report()
    {
        Assert.Equal("""{"changes":"not json"}""", ToolArguments.Repair("""{"changes":"not json"}""", Schema));
        Assert.Equal("not json at all", ToolArguments.Repair("not json at all", Schema));
    }

    [Fact]
    public void A_response_is_repaired_against_the_schema_of_the_tool_each_call_names()
    {
        var request = new LlmCompletionRequest
        {
            Messages = [],
            Tools = [new LlmToolDefinition { Name = "propose", Description = "", JsonSchema = Schema }, new LlmToolDefinition { Name = "other", Description = "", JsonSchema = """{"type":"object","properties":{"note":{"type":"string"}}}""" }]
        };
        var good = new ToolCall { Id = "a", Name = "other", ArgumentsJson = """{"note":"[1,2]"}""" };
        var bad = new ToolCall { Id = "b", Name = "propose", ArgumentsJson = """{"changes":"[{\"op\":\"set\"}]"}""" };
        var unknown = new ToolCall { Id = "c", Name = "not_offered", ArgumentsJson = """{"changes":"[]"}""" };

        var response = ToolArguments.Repair(request, new LlmCompletionResponse { ToolCalls = [good, bad, unknown] });

        Assert.Same(good, response.ToolCalls![0]); // a string where a string belongs stays as it is
        Assert.Equal("set", JsonNode.Parse(response.ToolCalls[1].ArgumentsJson)!["changes"]![0]!["op"]!.GetValue<string>());
        Assert.Same(unknown, response.ToolCalls[2]); // no schema to go by
    }

    [Fact]
    public void A_valid_response_is_returned_as_it_was()
    {
        var request = new LlmCompletionRequest { Messages = [], Tools = [new LlmToolDefinition { Name = "propose", Description = "", JsonSchema = Schema }] };
        var response = new LlmCompletionResponse { ToolCalls = [new ToolCall { Id = "a", Name = "propose", ArgumentsJson = """{"changes":[]}""" }] };
        Assert.Same(response, ToolArguments.Repair(request, response));
    }
}
