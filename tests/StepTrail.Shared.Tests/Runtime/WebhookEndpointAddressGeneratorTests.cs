using StepTrail.Api.Services;
using Xunit;

namespace StepTrail.Shared.Tests.Runtime;

public class WebhookEndpointAddressGeneratorTests
{
    [Fact]
    public void BuildRelativePath_UsesWebhookRouteKeyAsPathSegment()
    {
        var path = WebhookEndpointAddressGenerator.BuildRelativePath("partner-events");

        Assert.Equal("/webhooks/partner-events", path);
    }

    [Fact]
    public void BuildRelativePath_EncodesUnsafeCharacters()
    {
        var path = WebhookEndpointAddressGenerator.BuildRelativePath("partner events");

        Assert.Equal("/webhooks/partner%20events", path);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void BuildRelativePath_Throws_WhenRouteKeyIsMissing(string routeKey)
    {
        var ex = Assert.Throws<ArgumentException>(() => WebhookEndpointAddressGenerator.BuildRelativePath(routeKey));

        Assert.Equal("routeKey", ex.ParamName);
    }
}
