using System.Text.Json;
using StepTrail.Api.Services;
using StepTrail.Shared.Definitions;
using Xunit;

namespace StepTrail.Shared.Tests.Runtime;

public class WebhookIdempotencyKeyExtractorTests
{
    private readonly WebhookIdempotencyKeyExtractor _extractor = new();

    [Fact]
    public void ExtractOrNone_FromHeader_ReturnsHeaderValue()
    {
        var payload = JsonSerializer.Deserialize<JsonElement>("""{"eventId":"evt_123"}""");

        var value = _extractor.ExtractOrNone(
            payload,
            new Dictionary<string, string> { ["x-delivery-id"] = "delivery-123" },
            new WebhookIdempotencyKeyExtractionConfiguration("headers.x-delivery-id"));

        Assert.Equal("delivery-123", value);
    }

    [Fact]
    public void ExtractOrNone_FromBody_ReturnsScalarBodyValue()
    {
        var payload = JsonSerializer.Deserialize<JsonElement>("""{"event":{"id":"evt_123"}}""");

        var value = _extractor.ExtractOrNone(
            payload,
            null,
            new WebhookIdempotencyKeyExtractionConfiguration("body.event.id"));

        Assert.Equal("evt_123", value);
    }

    [Fact]
    public void ExtractOrNone_Throws_WhenConfiguredValueIsMissing()
    {
        var payload = JsonSerializer.Deserialize<JsonElement>("""{"eventId":"evt_123"}""");

        var ex = Assert.Throws<WebhookTriggerIdempotencyExtractionException>(() => _extractor.ExtractOrNone(
            payload,
            null,
            new WebhookIdempotencyKeyExtractionConfiguration("headers.x-delivery-id")));

        Assert.Contains("x-delivery-id", ex.Message, StringComparison.Ordinal);
    }
}
