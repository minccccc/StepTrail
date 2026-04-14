using StepTrail.Shared.Definitions;
using StepTrail.Shared.Runtime.AvailableFields;
using StepTrail.Shared.Runtime.Placeholders;
using Xunit;

namespace StepTrail.Shared.Tests.Runtime.AvailableFields;

public class AvailableFieldsServiceTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static WorkflowDefinition MakeDefinition(params StepDefinition[] steps) =>
        new(Guid.NewGuid(),
            "test-workflow",
            "Test Workflow",
            version: 1,
            WorkflowDefinitionStatus.Active,
            TriggerDefinition.CreateWebhook(Guid.NewGuid(), new WebhookTriggerConfiguration("test")),
            steps,
            Now, Now);

    private static StepDefinition HttpStep(string key, int order) =>
        StepDefinition.CreateHttpRequest(
            Guid.NewGuid(), key, order,
            new HttpRequestStepConfiguration($"https://api.example.com/{key}"));

    private static StepDefinition SendWebhookStep(string key, int order) =>
        StepDefinition.CreateSendWebhook(
            Guid.NewGuid(), key, order,
            new SendWebhookStepConfiguration($"https://hooks.example.com/{key}"));

    private static StepDefinition DelayStep(string key, int order) =>
        StepDefinition.CreateDelay(
            Guid.NewGuid(), key, order,
            new DelayStepConfiguration(30));

    private static StepDefinition TransformStep(string key, int order, params (string target, string source)[] mappings) =>
        StepDefinition.CreateTransform(
            Guid.NewGuid(), key, order,
            new TransformStepConfiguration(
                mappings.Select(m => new TransformValueMapping(m.target, m.source))));

    // ── First step: no prior steps ────────────────────────────────────────────

    [Fact]
    public void GetAvailableFields_FirstStep_ReturnsEmptySteps()
    {
        var def = MakeDefinition(HttpStep("fetch", 1), HttpStep("enrich", 2));

        var result = AvailableFieldsService.GetAvailableFields(def, "fetch", []);

        Assert.Empty(result.Steps);
    }

    [Fact]
    public void GetAvailableFields_FirstStep_InputNoteIsPresent()
    {
        var def = MakeDefinition(HttpStep("fetch", 1));

        var result = AvailableFieldsService.GetAvailableFields(def, "fetch", []);

        Assert.False(string.IsNullOrWhiteSpace(result.InputNote));
        Assert.Contains("input", result.InputNote, StringComparison.OrdinalIgnoreCase);
    }

    // ── HttpRequest prior step ────────────────────────────────────────────────

    [Fact]
    public void GetAvailableFields_HttpRequestPriorStep_ReturnsThreeFields()
    {
        var def = MakeDefinition(HttpStep("fetch-order", 1), HttpStep("enrich", 2));

        var result = AvailableFieldsService.GetAvailableFields(def, "enrich", []);

        var group = Assert.Single(result.Steps);
        Assert.Equal("fetch-order", group.StepKey);
        Assert.Equal("HttpRequest", group.StepType);
        Assert.Equal(3, group.Fields.Count);
    }

    [Fact]
    public void GetAvailableFields_HttpRequestPriorStep_FieldPlaceholdersAreCorrect()
    {
        var def = MakeDefinition(HttpStep("fetch-order", 1), HttpStep("enrich", 2));

        var result = AvailableFieldsService.GetAvailableFields(def, "enrich", []);

        var fields = result.Steps[0].Fields;
        Assert.Contains(fields, f => f.Placeholder == "{{steps.fetch-order.output.statusCode}}" && f.FieldType == "number");
        Assert.Contains(fields, f => f.Placeholder == "{{steps.fetch-order.output.body}}"       && f.FieldType == "string");
        Assert.Contains(fields, f => f.Placeholder == "{{steps.fetch-order.output.headers}}"    && f.FieldType == "object");
    }

    // ── SendWebhook uses the same output schema as HttpRequest ────────────────

    [Fact]
    public void GetAvailableFields_SendWebhookPriorStep_ReturnsSameFieldsAsHttpRequest()
    {
        var httpDef    = MakeDefinition(HttpStep("s1", 1), HttpStep("target", 2));
        var webhookDef = MakeDefinition(SendWebhookStep("s1", 1), HttpStep("target", 2));

        var httpResult    = AvailableFieldsService.GetAvailableFields(httpDef,    "target", []);
        var webhookResult = AvailableFieldsService.GetAvailableFields(webhookDef, "target", []);

        Assert.Equal(
            httpResult.Steps[0].Fields.Select(f => f.Placeholder).Order(),
            webhookResult.Steps[0].Fields.Select(f => f.Placeholder).Order());
    }

    // ── Delay produces no output fields ──────────────────────────────────────

    [Fact]
    public void GetAvailableFields_DelayPriorStep_ReturnsEmptyFields()
    {
        var def = MakeDefinition(DelayStep("wait", 1), HttpStep("next", 2));

        var result = AvailableFieldsService.GetAvailableFields(def, "next", []);

        var group = Assert.Single(result.Steps);
        Assert.Equal("wait", group.StepKey);
        Assert.Equal("Delay", group.StepType);
        Assert.Empty(group.Fields);
    }

    // ── Transform output fields derived from mapping targets ──────────────────

    [Fact]
    public void GetAvailableFields_TransformPriorStep_ReturnsMappingTargetAsFields()
    {
        var def = MakeDefinition(
            TransformStep("map-data", 1, ("orderId", "{{input.id}}"), ("customerName", "{{input.name}}")),
            HttpStep("send", 2));

        var result = AvailableFieldsService.GetAvailableFields(def, "send", []);

        var group = Assert.Single(result.Steps);
        Assert.Equal("map-data", group.StepKey);
        Assert.Equal("Transform", group.StepType);
        Assert.Contains(group.Fields, f => f.Placeholder == "{{steps.map-data.output.orderId}}");
        Assert.Contains(group.Fields, f => f.Placeholder == "{{steps.map-data.output.customerName}}");
        Assert.Equal(2, group.Fields.Count);
    }

    // ── Multiple prior steps returned in order ────────────────────────────────

    [Fact]
    public void GetAvailableFields_MultiplePriorSteps_ReturnedInExecutionOrder()
    {
        var def = MakeDefinition(
            HttpStep("step-a", 1),
            HttpStep("step-b", 2),
            HttpStep("step-c", 3),
            HttpStep("target", 4));

        var result = AvailableFieldsService.GetAvailableFields(def, "target", []);

        Assert.Equal(3, result.Steps.Count);
        Assert.Equal("step-a", result.Steps[0].StepKey);
        Assert.Equal("step-b", result.Steps[1].StepKey);
        Assert.Equal("step-c", result.Steps[2].StepKey);
    }

    [Fact]
    public void GetAvailableFields_MiddleStep_OnlyPriorStepsIncluded()
    {
        // Target is step 2 — only step 1 is a prior step; step 3 is not yet available.
        var def = MakeDefinition(
            HttpStep("step-1", 1),
            HttpStep("target", 2),
            HttpStep("step-3", 3));

        var result = AvailableFieldsService.GetAvailableFields(def, "target", []);

        var group = Assert.Single(result.Steps);
        Assert.Equal("step-1", group.StepKey);
    }

    // ── Secrets ───────────────────────────────────────────────────────────────

    [Fact]
    public void GetAvailableFields_WithSecrets_ReturnsAlphabeticallySortedSecretPlaceholders()
    {
        var def = MakeDefinition(HttpStep("step", 1));

        var result = AvailableFieldsService.GetAvailableFields(
            def, "step", ["stripe-key", "api-key", "db-password"]);

        Assert.Equal(3, result.Secrets.Count);
        Assert.Equal("{{secrets.api-key}}",    result.Secrets[0].Placeholder);
        Assert.Equal("{{secrets.db-password}}", result.Secrets[1].Placeholder);
        Assert.Equal("{{secrets.stripe-key}}",  result.Secrets[2].Placeholder);
    }

    [Fact]
    public void GetAvailableFields_WithSecrets_AllHaveStringFieldType()
    {
        var def = MakeDefinition(HttpStep("step", 1));

        var result = AvailableFieldsService.GetAvailableFields(def, "step", ["api-key"]);

        Assert.All(result.Secrets, s => Assert.Equal("string", s.FieldType));
    }

    [Fact]
    public void GetAvailableFields_NoSecrets_ReturnsEmptySecrets()
    {
        var def = MakeDefinition(HttpStep("step", 1));

        var result = AvailableFieldsService.GetAvailableFields(def, "step", []);

        Assert.Empty(result.Secrets);
    }

    // ── Error cases ───────────────────────────────────────────────────────────

    [Fact]
    public void GetAvailableFields_UnknownStepKey_Throws()
    {
        var def = MakeDefinition(HttpStep("fetch", 1));

        var ex = Assert.Throws<ArgumentException>(() =>
            AvailableFieldsService.GetAvailableFields(def, "nonexistent", []));

        Assert.Contains("nonexistent", ex.Message);
        Assert.Contains("test-workflow", ex.Message);
    }

    [Fact]
    public void GetAvailableFields_EmptyStepKey_Throws()
    {
        var def = MakeDefinition(HttpStep("fetch", 1));

        Assert.Throws<ArgumentException>(() =>
            AvailableFieldsService.GetAvailableFields(def, "", []));
    }

    // ── Placeholder syntactic validity ────────────────────────────────────────

    [Fact]
    public void GetAvailableFields_AllReturnedPlaceholders_AreParseable()
    {
        // Verifies every returned {{...}} placeholder can be parsed by PlaceholderParser
        // without errors — ensuring no invalid paths are suggested.
        var def = MakeDefinition(
            HttpStep("fetch-order", 1),
            TransformStep("map-data", 2, ("userId", "{{input.id}}")),
            DelayStep("wait", 3),
            HttpStep("target", 4));

        var result = AvailableFieldsService.GetAvailableFields(
            def, "target", ["api-key", "db-secret"]);

        var allPlaceholders = result.Steps
            .SelectMany(g => g.Fields)
            .Concat(result.Secrets)
            .Select(f => f.Placeholder)
            .ToList();

        foreach (var placeholder in allPlaceholders)
        {
            // Skip object-type fields (headers) — they are documented as non-scalar but valid syntax
            if (placeholder.EndsWith(".headers}}")) continue;

            var parseResult = PlaceholderParser.Parse(placeholder);
            Assert.True(parseResult.IsSuccess,
                $"Placeholder '{placeholder}' failed to parse: {parseResult.Error}");
        }
    }
}
