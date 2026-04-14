using StepTrail.Shared.Entities;
using StepTrail.Shared.Runtime;
using StepTrail.Shared.Runtime.Placeholders;
using Xunit;

namespace StepTrail.Shared.Tests.Runtime.Placeholders;

public class PlaceholderResolverTests
{
    private static readonly PlaceholderResolver Resolver = new();
    private static readonly IReadOnlyDictionary<string, string> NoSecrets =
        new Dictionary<string, string>();

    // ── Null / empty ──────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_NullTemplate_ReturnsEmptyString()
    {
        var result = Resolver.Resolve(null, EmptyState(), NoSecrets);

        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, result.Value);
    }

    [Fact]
    public void Resolve_EmptyTemplate_ReturnsEmptyString()
    {
        var result = Resolver.Resolve("", EmptyState(), NoSecrets);

        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, result.Value);
    }

    // ── Pure literal ──────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_PureLiteralTemplate_ReturnsLiteralUnchanged()
    {
        var result = Resolver.Resolve("https://api.example.com/ping", EmptyState(), NoSecrets);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://api.example.com/ping", result.Value);
    }

    // ── Input root ────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_InputPlaceholder_ReturnsFieldValue()
    {
        var state  = StateWithInput("""{"customerId": "cus_123"}""");
        var result = Resolver.Resolve("{{input.customerId}}", state, NoSecrets);

        Assert.True(result.IsSuccess);
        Assert.Equal("cus_123", result.Value);
    }

    [Fact]
    public void Resolve_NestedInputPlaceholder_NavigatesPath()
    {
        var state  = StateWithInput("""{"customer": {"address": {"city": "London"}}}""");
        var result = Resolver.Resolve("{{input.customer.address.city}}", state, NoSecrets);

        Assert.True(result.IsSuccess);
        Assert.Equal("London", result.Value);
    }

    [Fact]
    public void Resolve_InputPlaceholder_NumericValue_ReturnsStringRepresentation()
    {
        var state  = StateWithInput("""{"amount": 49}""");
        var result = Resolver.Resolve("{{input.amount}}", state, NoSecrets);

        Assert.True(result.IsSuccess);
        Assert.Equal("49", result.Value);
    }

    [Fact]
    public void Resolve_InputPlaceholder_BooleanValue_ReturnsStringRepresentation()
    {
        var state  = StateWithInput("""{"active": true}""");
        var result = Resolver.Resolve("{{input.active}}", state, NoSecrets);

        Assert.True(result.IsSuccess);
        Assert.Equal("true", result.Value);
    }

    [Fact]
    public void Resolve_InputPlaceholder_NullInput_ReturnsFailure()
    {
        var state  = EmptyState();  // Input is null
        var result = Resolver.Resolve("{{input.customerId}}", state, NoSecrets);

        Assert.False(result.IsSuccess);
        Assert.Contains("null", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_InputPlaceholder_MissingField_ReturnsFailure()
    {
        var state  = StateWithInput("""{"otherField": "x"}""");
        var result = Resolver.Resolve("{{input.customerId}}", state, NoSecrets);

        Assert.False(result.IsSuccess);
        Assert.Contains("customerId", result.Error);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_InputPlaceholder_ObjectValue_ReturnsFailure()
    {
        var state  = StateWithInput("""{"customer": {"id": "cus_1"}}""");
        var result = Resolver.Resolve("{{input.customer}}", state, NoSecrets);

        Assert.False(result.IsSuccess);
        Assert.Contains("object", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_InputPlaceholder_ArrayValue_ReturnsFailure()
    {
        var state  = StateWithInput("""{"items": [1, 2, 3]}""");
        var result = Resolver.Resolve("{{input.items}}", state, NoSecrets);

        Assert.False(result.IsSuccess);
        Assert.Contains("array", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_InputPlaceholder_NullJsonValue_ReturnsFailure()
    {
        var state  = StateWithInput("""{"customerId": null}""");
        var result = Resolver.Resolve("{{input.customerId}}", state, NoSecrets);

        Assert.False(result.IsSuccess);
        Assert.Contains("null", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Steps root ────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_StepsPlaceholder_ReturnsFieldFromStepOutput()
    {
        var state = StateWithStep(
            "fetch-order",
            output: """{"orderId": "ord_789", "status": "confirmed"}""");

        var result = Resolver.Resolve("{{steps.fetch-order.output.orderId}}", state, NoSecrets);

        Assert.True(result.IsSuccess);
        Assert.Equal("ord_789", result.Value);
    }

    [Fact]
    public void Resolve_StepsPlaceholder_NestedPath_NavigatesCorrectly()
    {
        var state = StateWithStep(
            "enrich",
            output: """{"profile": {"tier": "gold"}}""");

        var result = Resolver.Resolve("{{steps.enrich.output.profile.tier}}", state, NoSecrets);

        Assert.True(result.IsSuccess);
        Assert.Equal("gold", result.Value);
    }

    [Fact]
    public void Resolve_StepsPlaceholder_StepNotInState_ReturnsFailure()
    {
        var state  = EmptyState();
        var result = Resolver.Resolve("{{steps.fetch-order.output.orderId}}", state, NoSecrets);

        Assert.False(result.IsSuccess);
        Assert.Contains("fetch-order", result.Error);
    }

    [Fact]
    public void Resolve_StepsPlaceholder_StepHasNoOutput_ReturnsFailure()
    {
        var state = StateWithStep("fetch-order", output: null);
        var result = Resolver.Resolve("{{steps.fetch-order.output.orderId}}", state, NoSecrets);

        Assert.False(result.IsSuccess);
        Assert.Contains("fetch-order", result.Error);
        Assert.Contains("no output", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_StepsPlaceholder_FieldMissingFromOutput_ReturnsFailure()
    {
        var state = StateWithStep("fetch-order", output: """{"status": "confirmed"}""");
        var result = Resolver.Resolve("{{steps.fetch-order.output.orderId}}", state, NoSecrets);

        Assert.False(result.IsSuccess);
        Assert.Contains("orderId", result.Error);
    }

    // ── Secrets root ──────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_SecretsPlaceholder_ReturnsSecretValue()
    {
        var secrets = new Dictionary<string, string> { ["api-key"] = "sk-abc123" };
        var result  = Resolver.Resolve("{{secrets.api-key}}", EmptyState(), secrets);

        Assert.True(result.IsSuccess);
        Assert.Equal("sk-abc123", result.Value);
    }

    [Fact]
    public void Resolve_SecretsPlaceholder_UnknownSecret_ReturnsFailure()
    {
        var result = Resolver.Resolve("{{secrets.api-key}}", EmptyState(), NoSecrets);

        Assert.False(result.IsSuccess);
        Assert.Contains("api-key", result.Error);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Mixed templates ───────────────────────────────────────────────────────

    [Fact]
    public void Resolve_UrlWithEmbeddedInputPlaceholder_AssemblesCorrectly()
    {
        var state  = StateWithInput("""{"orderId": "ord_789"}""");
        var result = Resolver.Resolve(
            "https://api.example.com/orders/{{input.orderId}}/items",
            state, NoSecrets);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://api.example.com/orders/ord_789/items", result.Value);
    }

    [Fact]
    public void Resolve_TemplateWithMultiplePlaceholders_ResolvesAll()
    {
        var state = StateWithInputAndStep(
            input: """{"firstName": "Alice"}""",
            stepName: "lookup",
            stepOutput: """{"tier": "gold"}""");

        var secrets = new Dictionary<string, string> { ["greeting"] = "Hello" };

        var result = Resolver.Resolve(
            "{{secrets.greeting}}, {{input.firstName}} — tier: {{steps.lookup.output.tier}}",
            state, secrets);

        Assert.True(result.IsSuccess);
        Assert.Equal("Hello, Alice — tier: gold", result.Value);
    }

    [Fact]
    public void Resolve_FirstPlaceholderFails_ReturnsFailureWithoutResolvingRest()
    {
        // input.missing does not exist — should fail without touching the secrets placeholder
        var state  = StateWithInput("""{"other": "x"}""");
        var result = Resolver.Resolve(
            "{{input.missing}} and {{secrets.api-key}}",
            state, NoSecrets);

        Assert.False(result.IsSuccess);
        Assert.Contains("missing", result.Error);
    }

    // ── Malformed template propagation ────────────────────────────────────────

    [Fact]
    public void Resolve_MalformedTemplate_ReturnsParseErrorAsFailure()
    {
        var result = Resolver.Resolve("{{input.unclosed", EmptyState(), NoSecrets);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    // ── State builders ────────────────────────────────────────────────────────

    private static WorkflowState EmptyState() =>
        new(
            new WorkflowStateMetadata(
                Guid.NewGuid(), "test-workflow", 1,
                WorkflowInstanceStatus.Running,
                DateTimeOffset.UtcNow, null),
            triggerData: null,
            input: null,
            steps: new Dictionary<string, WorkflowStepState>());

    private static WorkflowState StateWithInput(string inputJson) =>
        new(
            new WorkflowStateMetadata(
                Guid.NewGuid(), "test-workflow", 1,
                WorkflowInstanceStatus.Running,
                DateTimeOffset.UtcNow, null),
            triggerData: null,
            input: inputJson,
            steps: new Dictionary<string, WorkflowStepState>());

    private static WorkflowState StateWithStep(string stepName, string? output) =>
        new(
            new WorkflowStateMetadata(
                Guid.NewGuid(), "test-workflow", 1,
                WorkflowInstanceStatus.Running,
                DateTimeOffset.UtcNow, null),
            triggerData: null,
            input: null,
            steps: new Dictionary<string, WorkflowStepState>
            {
                [stepName] = new WorkflowStepState(
                    stepName,
                    WorkflowStepExecutionStatus.Completed,
                    output: output,
                    error: null,
                    attempts: [])
            });

    private static WorkflowState StateWithInputAndStep(
        string input, string stepName, string stepOutput) =>
        new(
            new WorkflowStateMetadata(
                Guid.NewGuid(), "test-workflow", 1,
                WorkflowInstanceStatus.Running,
                DateTimeOffset.UtcNow, null),
            triggerData: null,
            input: input,
            steps: new Dictionary<string, WorkflowStepState>
            {
                [stepName] = new WorkflowStepState(
                    stepName,
                    WorkflowStepExecutionStatus.Completed,
                    output: stepOutput,
                    error: null,
                    attempts: [])
            });
}
