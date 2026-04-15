using System.Text.Json;
using StepTrail.Api.Services;
using StepTrail.Shared.Definitions;
using Xunit;

namespace StepTrail.Shared.Tests.Runtime;

public class WebhookInputMapperTests
{
    private readonly WebhookInputMapper _mapper = new();

    [Fact]
    public void MapOrPassThrough_WhenMappingsConfigured_MapsBodyHeadersAndQuery()
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(
            """{"event":{"id":"evt_123"},"customer":{"id":"cus_001"}}""");

        var result = _mapper.MapOrPassThrough(
            payload,
            new Dictionary<string, string> { ["x-request-id"] = "req_789" },
            new Dictionary<string, string> { ["delivery"] = "retry-1" },
            [
                new WebhookInputMapping("eventId", "body.event.id"),
                new WebhookInputMapping("customer.id", "body.customer.id"),
                new WebhookInputMapping("requestId", "headers.x-request-id"),
                new WebhookInputMapping("delivery", "query.delivery")
            ]);

        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("evt_123", doc.RootElement.GetProperty("eventId").GetString());
        Assert.Equal("cus_001", doc.RootElement.GetProperty("customer").GetProperty("id").GetString());
        Assert.Equal("req_789", doc.RootElement.GetProperty("requestId").GetString());
        Assert.Equal("retry-1", doc.RootElement.GetProperty("delivery").GetString());
    }

    [Fact]
    public void MapOrPassThrough_Throws_WhenSourcePathIsMissing()
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(
            """{"event":{"id":"evt_123"}}""");

        var ex = Assert.Throws<WebhookTriggerInputMappingException>(() => _mapper.MapOrPassThrough(
            payload,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            [
                new WebhookInputMapping("customerId", "body.customer.id")
            ]));

        Assert.Contains("body.customer.id", ex.Message, StringComparison.Ordinal);
    }
}
