using StepTrail.Shared.Runtime.Placeholders;
using Xunit;

namespace StepTrail.Shared.Tests.Runtime.Placeholders;

public class PlaceholderParserTests
{
    // ── Null / empty ──────────────────────────────────────────────────────────

    [Fact]
    public void Parse_NullTemplate_ReturnsEmptySegments()
    {
        var result = PlaceholderParser.Parse(null);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Segments);
    }

    [Fact]
    public void Parse_EmptyTemplate_ReturnsEmptySegments()
    {
        var result = PlaceholderParser.Parse("");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Segments);
    }

    // ── Pure literal ──────────────────────────────────────────────────────────

    [Fact]
    public void Parse_PureLiteral_ReturnsSingleLiteralSegment()
    {
        var result = PlaceholderParser.Parse("https://api.example.com/ping");

        Assert.True(result.IsSuccess);
        var segment = Assert.Single(result.Segments);
        var literal = Assert.IsType<LiteralSegment>(segment);
        Assert.Equal("https://api.example.com/ping", literal.Value);
    }

    // ── Input root ────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_InputPlaceholder_ReturnsPlaceholderSegment()
    {
        var result = PlaceholderParser.Parse("{{input.customerId}}");

        Assert.True(result.IsSuccess);
        var segment = Assert.Single(result.Segments);
        var placeholder = Assert.IsType<PlaceholderSegment>(segment);
        Assert.Equal(PlaceholderRoot.Input, placeholder.Root);
        Assert.Equal(["customerId"], placeholder.Path);
    }

    [Fact]
    public void Parse_NestedInputPlaceholder_ReturnsFullPath()
    {
        var result = PlaceholderParser.Parse("{{input.customer.address.city}}");

        Assert.True(result.IsSuccess);
        var segment = Assert.Single(result.Segments);
        var placeholder = Assert.IsType<PlaceholderSegment>(segment);
        Assert.Equal(PlaceholderRoot.Input, placeholder.Root);
        Assert.Equal(["customer", "address", "city"], placeholder.Path);
    }

    // ── Steps root ────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_StepsPlaceholder_ReturnsParsedStepAndPath()
    {
        var result = PlaceholderParser.Parse("{{steps.fetch_order.output.orderId}}");

        Assert.True(result.IsSuccess);
        var segment = Assert.Single(result.Segments);
        var placeholder = Assert.IsType<PlaceholderSegment>(segment);
        Assert.Equal(PlaceholderRoot.Steps, placeholder.Root);
        Assert.Equal("fetch_order", placeholder.StepName);
        Assert.Equal(["output", "orderId"], placeholder.NavigationPath);
    }

    [Fact]
    public void Parse_StepsPlaceholder_NestedPath_ParsesCorrectly()
    {
        var result = PlaceholderParser.Parse("{{steps.enrich-customer.output.profile.tier}}");

        Assert.True(result.IsSuccess);
        var ph = Assert.Single(result.Segments) as PlaceholderSegment;
        Assert.NotNull(ph);
        Assert.Equal("enrich-customer", ph!.StepName);
        Assert.Equal(["output", "profile", "tier"], ph.NavigationPath);
    }

    // ── Secrets root ──────────────────────────────────────────────────────────

    [Fact]
    public void Parse_SecretsPlaceholder_ReturnsSingleNameSegment()
    {
        var result = PlaceholderParser.Parse("{{secrets.api-key}}");

        Assert.True(result.IsSuccess);
        var segment = Assert.Single(result.Segments);
        var placeholder = Assert.IsType<PlaceholderSegment>(segment);
        Assert.Equal(PlaceholderRoot.Secrets, placeholder.Root);
        Assert.Equal(["api-key"], placeholder.Path);
    }

    // ── Mixed literal + placeholder ───────────────────────────────────────────

    [Fact]
    public void Parse_UrlWithEmbeddedPlaceholder_ProducesLiteralAndPlaceholderSegments()
    {
        var result = PlaceholderParser.Parse("https://api.example.com/orders/{{input.orderId}}/items");

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Segments.Count);
        Assert.Equal("https://api.example.com/orders/", ((LiteralSegment)result.Segments[0]).Value);
        Assert.Equal(PlaceholderRoot.Input, ((PlaceholderSegment)result.Segments[1]).Root);
        Assert.Equal("/items", ((LiteralSegment)result.Segments[2]).Value);
    }

    [Fact]
    public void Parse_MultiplePlaceholders_ProducesAllSegmentsInOrder()
    {
        var result = PlaceholderParser.Parse("{{input.firstName}} {{input.lastName}}");

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Segments.Count);
        Assert.IsType<PlaceholderSegment>(result.Segments[0]);
        Assert.Equal(" ", ((LiteralSegment)result.Segments[1]).Value);
        Assert.IsType<PlaceholderSegment>(result.Segments[2]);
    }

    [Fact]
    public void Parse_PlaceholderOnlyTemplate_ProducesSinglePlaceholderSegment()
    {
        var result = PlaceholderParser.Parse("{{input.id}}");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Segments);
        Assert.IsType<PlaceholderSegment>(result.Segments[0]);
    }

    // ── Whitespace tolerance ──────────────────────────────────────────────────

    [Fact]
    public void Parse_PlaceholderWithLeadingTrailingWhitespace_TrimsAndParsesCorrectly()
    {
        var result = PlaceholderParser.Parse("{{  input.customerId  }}");

        Assert.True(result.IsSuccess);
        var ph = Assert.Single(result.Segments) as PlaceholderSegment;
        Assert.NotNull(ph);
        Assert.Equal(PlaceholderRoot.Input, ph!.Root);
        Assert.Equal(["customerId"], ph.Path);
    }

    // ── Error cases ───────────────────────────────────────────────────────────

    [Fact]
    public void Parse_UnclosedPlaceholder_ReturnsFailure()
    {
        var result = PlaceholderParser.Parse("Hello {{input.name");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Contains("nclosed", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_EmptyPlaceholder_ReturnsFailure()
    {
        var result = PlaceholderParser.Parse("{{}}");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Contains("mpty", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_WhitespaceOnlyPlaceholder_ReturnsFailure()
    {
        var result = PlaceholderParser.Parse("{{   }}");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_UnsupportedRoot_ReturnsFailure()
    {
        var result = PlaceholderParser.Parse("{{output.field}}");

        Assert.False(result.IsSuccess);
        Assert.Contains("output", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nsupp", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RootOnlyNoPath_ReturnsFailure()
    {
        var result = PlaceholderParser.Parse("{{input}}");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_StepsWithOnlyStepName_ReturnsFailure()
    {
        // steps.<step_name> is not enough — needs accessor + field
        var result = PlaceholderParser.Parse("{{steps.my_step}}");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_StepsWithOnlyStepNameAndAccessor_ReturnsFailure()
    {
        // steps.<step_name>.output is not enough — needs a field segment too
        var result = PlaceholderParser.Parse("{{steps.my_step.output}}");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_SecretsWithMultiplePathSegments_ReturnsFailure()
    {
        var result = PlaceholderParser.Parse("{{secrets.group.key}}");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_EmptyPathSegmentFromConsecutiveDots_ReturnsFailure()
    {
        var result = PlaceholderParser.Parse("{{input..field}}");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_InvalidCharactersInSegment_ReturnsFailure()
    {
        var result = PlaceholderParser.Parse("{{input.field name}}");

        Assert.False(result.IsSuccess);
        Assert.Contains("invalid", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── PlaceholderSegment equality ───────────────────────────────────────────

    [Fact]
    public void PlaceholderSegment_SameRootAndPath_AreEqual()
    {
        var a = new PlaceholderSegment(PlaceholderRoot.Input, ["customer", "id"]);
        var b = new PlaceholderSegment(PlaceholderRoot.Input, ["customer", "id"]);

        Assert.Equal(a, b);
    }

    [Fact]
    public void PlaceholderSegment_DifferentPath_AreNotEqual()
    {
        var a = new PlaceholderSegment(PlaceholderRoot.Input, ["customer", "id"]);
        var b = new PlaceholderSegment(PlaceholderRoot.Input, ["customer", "name"]);

        Assert.NotEqual(a, b);
    }
}
