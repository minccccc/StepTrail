using System.Text.Json;
using StepTrail.Shared.Entities;
using StepTrail.Shared.Runtime;
using StepTrail.Shared.Runtime.OutputModels;
using StepTrail.Shared.Runtime.Placeholders;
using Xunit;

namespace StepTrail.Shared.Tests.Runtime.OutputModels;

public class HttpRequestStepOutputTests
{
    [Fact]
    public void Serialize_ProducesCamelCasePropertyNames()
    {
        var output = new HttpRequestStepOutput
        {
            StatusCode = 200,
            Body = JsonSerializer.SerializeToElement("ok"),
            BodyText = "ok"
        };

        var json = JsonSerializer.Serialize(output);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("statusCode", out _));
        Assert.True(doc.RootElement.TryGetProperty("body", out _));
        Assert.True(doc.RootElement.TryGetProperty("bodyText", out _));
    }

    [Fact]
    public void Serialize_WithoutHeadersAndContentType_OmitsOptionalProperties()
    {
        var output = new HttpRequestStepOutput
        {
            StatusCode = 200,
            Body = JsonSerializer.SerializeToElement("ok"),
            BodyText = "ok",
            Headers = null,
            ContentType = null
        };

        var json = JsonSerializer.Serialize(output);
        using var doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.TryGetProperty("headers", out _));
        Assert.False(doc.RootElement.TryGetProperty("contentType", out _));
    }

    [Fact]
    public void Serialize_JsonBody_RoundTripsAsObjectAndPreservesBodyText()
    {
        var output = new HttpRequestStepOutput
        {
            StatusCode = 200,
            Body = ParseJsonElement("""{"subscriptionId":"sub_123","accepted":true}"""),
            BodyText = """{"subscriptionId":"sub_123","accepted":true}""",
            ContentType = "application/json",
            Headers = new Dictionary<string, string>
            {
                ["content-type"] = "application/json",
                ["x-request-id"] = "req_123"
            }
        };

        var json = JsonSerializer.Serialize(output);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(200, doc.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(JsonValueKind.Object, doc.RootElement.GetProperty("body").ValueKind);
        Assert.Equal("sub_123", doc.RootElement.GetProperty("body").GetProperty("subscriptionId").GetString());
        Assert.Equal("""{"subscriptionId":"sub_123","accepted":true}""", doc.RootElement.GetProperty("bodyText").GetString());
        Assert.Equal("application/json", doc.RootElement.GetProperty("contentType").GetString());
        Assert.Equal("req_123", doc.RootElement.GetProperty("headers").GetProperty("x-request-id").GetString());
    }

    [Fact]
    public void Serialize_TextBody_RoundTripsAsStringValueAndPreservesBodyText()
    {
        var output = new HttpRequestStepOutput
        {
            StatusCode = 502,
            Body = JsonSerializer.SerializeToElement("gateway error"),
            BodyText = "gateway error",
            ContentType = "text/plain"
        };

        var json = JsonSerializer.Serialize(output);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(502, doc.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("body").ValueKind);
        Assert.Equal("gateway error", doc.RootElement.GetProperty("body").GetString());
        Assert.Equal("gateway error", doc.RootElement.GetProperty("bodyText").GetString());
        Assert.Equal("text/plain", doc.RootElement.GetProperty("contentType").GetString());
    }

    [Fact]
    public void Serialize_OutputCanBeNavigatedByPlaceholderResolver_ForJsonBodyFields()
    {
        var output = new HttpRequestStepOutput
        {
            StatusCode = 200,
            Body = ParseJsonElement("""{"subscriptionId":"sub_123"}"""),
            BodyText = """{"subscriptionId":"sub_123"}""",
            ContentType = "application/json",
            Headers = new Dictionary<string, string> { ["content-type"] = "application/json" }
        };

        var state = new WorkflowState(
            new WorkflowStateMetadata(
                Guid.NewGuid(),
                "test-workflow",
                1,
                WorkflowInstanceStatus.Running,
                DateTimeOffset.UtcNow,
                null),
            triggerData: null,
            input: null,
            steps: new Dictionary<string, WorkflowStepState>
            {
                ["call-api"] = new WorkflowStepState(
                    "call-api",
                    WorkflowStepExecutionStatus.Completed,
                    JsonSerializer.Serialize(output),
                    error: null,
                    attempts: [])
            });

        var resolver = new PlaceholderResolver();
        var result = resolver.Resolve("{{steps.call-api.output.body.subscriptionId}}", state, new Dictionary<string, string>());

        Assert.True(result.IsSuccess);
        Assert.Equal("sub_123", result.Value);
    }

    private static JsonElement ParseJsonElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
