using System.Text.Json;
using StepTrail.Shared.Runtime.OutputModels;
using Xunit;

namespace StepTrail.Shared.Tests.Runtime.OutputModels;

public class HttpRequestStepOutputTests
{
    // ── JSON property names ───────────────────────────────────────────────────

    [Fact]
    public void Serialize_ProducesCamelCasePropertyNames()
    {
        var output = new HttpRequestStepOutput
        {
            StatusCode = 200,
            Body       = "ok"
        };

        var json = JsonSerializer.Serialize(output);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("statusCode", out _),
            "Expected 'statusCode' (camelCase)");
        Assert.True(doc.RootElement.TryGetProperty("body", out _),
            "Expected 'body' (camelCase)");
    }

    [Fact]
    public void Serialize_WithoutHeaders_OmitsHeadersProperty()
    {
        var output = new HttpRequestStepOutput
        {
            StatusCode = 200,
            Body       = "ok",
            Headers    = null
        };

        var json = JsonSerializer.Serialize(output);
        using var doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.TryGetProperty("headers", out _),
            "'headers' should be omitted when null");
    }

    [Fact]
    public void Serialize_WithHeaders_IncludesHeadersProperty()
    {
        var output = new HttpRequestStepOutput
        {
            StatusCode = 200,
            Body       = "ok",
            Headers    = new Dictionary<string, string>
            {
                ["content-type"] = "application/json",
                ["x-request-id"] = "abc123"
            }
        };

        var json = JsonSerializer.Serialize(output);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("headers", out var headersEl),
            "'headers' should be present when populated");
        Assert.Equal("application/json",
            headersEl.GetProperty("content-type").GetString());
        Assert.Equal("abc123",
            headersEl.GetProperty("x-request-id").GetString());
    }

    // ── Field values ──────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_StatusCodeAndBody_RoundTrip()
    {
        var output = new HttpRequestStepOutput { StatusCode = 404, Body = "not found" };

        var json = JsonSerializer.Serialize(output);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(404,          doc.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal("not found",  doc.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public void Serialize_EmptyBody_ProducesEmptyString()
    {
        var output = new HttpRequestStepOutput { StatusCode = 204, Body = string.Empty };

        var json = JsonSerializer.Serialize(output);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(string.Empty, doc.RootElement.GetProperty("body").GetString());
    }

    // ── Placeholder compatibility ─────────────────────────────────────────────

    [Fact]
    public void Serialize_OutputCanBeNavigatedByPlaceholderResolver()
    {
        // This test verifies the JSON shape is compatible with PlaceholderResolver.NavigateJson.
        // The resolver navigates by property name using System.Text.Json — so names must match exactly.
        var output = new HttpRequestStepOutput
        {
            StatusCode = 200,
            Body       = "hello",
            Headers    = new Dictionary<string, string> { ["content-type"] = "text/plain" }
        };

        var json = JsonSerializer.Serialize(output);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // {{steps.X.output.statusCode}}
        Assert.Equal(JsonValueKind.Number,  root.GetProperty("statusCode").ValueKind);
        // {{steps.X.output.body}}
        Assert.Equal(JsonValueKind.String,  root.GetProperty("body").ValueKind);
        // {{steps.X.output.headers}} is an object — not directly a scalar placeholder target
        Assert.Equal(JsonValueKind.Object,  root.GetProperty("headers").ValueKind);
    }
}
