using StepTrail.Shared.Entities;
using StepTrail.Shared.Runtime;
using StepTrail.Shared.Workflows;
using System.Text.Json;
using Xunit;

namespace StepTrail.Shared.Tests.Runtime;

public class StepExecutionContractTests
{
    [Fact]
    public void Success_CreatesSucceededResultWithoutFailure()
    {
        var result = StepExecutionResult.Success("""{"ok":true}""");

        Assert.True(result.IsSuccess);
        Assert.Equal(StepExecutionOutcome.Succeeded, result.Outcome);
        Assert.Equal("""{"ok":true}""", result.Output);
        Assert.Null(result.Failure);
    }

    [Fact]
    public void TransientFailure_CreatesFailedResultWithClassificationAndRetryHint()
    {
        var result = StepExecutionResult.TransientFailure(
            "remote service unavailable",
            output: """{"statusCode":503}""",
            retryDelayHint: TimeSpan.FromSeconds(30));

        Assert.False(result.IsSuccess);
        Assert.Equal(StepExecutionOutcome.Failed, result.Outcome);
        Assert.Equal("""{"statusCode":503}""", result.Output);
        Assert.NotNull(result.Failure);
        Assert.Equal(StepExecutionFailureClassification.TransientFailure, result.Failure!.Classification);
        Assert.Equal("remote service unavailable", result.Failure.Message);
        Assert.Equal(TimeSpan.FromSeconds(30), result.Failure.RetryDelayHint);
    }

    [Fact]
    public void CompleteWorkflow_CreatesSucceededResultWithCompletionContinuation()
    {
        var result = StepExecutionResult.CompleteWorkflow("""{"matched":false}""");

        Assert.True(result.IsSuccess);
        Assert.Equal(StepExecutionOutcome.Succeeded, result.Outcome);
        Assert.Equal(StepExecutionContinuation.CompleteWorkflow, result.Continuation);
        Assert.Equal("""{"matched":false}""", result.Output);
    }

    [Fact]
    public void WaitUntil_CreatesSucceededResultWithResumeTime()
    {
        var resumeAtUtc = new DateTimeOffset(2026, 4, 15, 13, 0, 0, TimeSpan.Zero);

        var result = StepExecutionResult.WaitUntil(
            resumeAtUtc,
            """{"delayType":"fixed"}""");

        Assert.True(result.IsSuccess);
        Assert.Equal(StepExecutionOutcome.Succeeded, result.Outcome);
        Assert.Equal(StepExecutionContinuation.ContinueWorkflow, result.Continuation);
        Assert.Equal(resumeAtUtc, result.ResumeAtUtc);
        Assert.Equal("""{"delayType":"fixed"}""", result.Output);
    }

    [Fact]
    public void ResolveTemplate_UsesSharedPlaceholderInfrastructure()
    {
        var request = new StepExecutionRequest
        {
            WorkflowInstanceId = Guid.NewGuid(),
            StepExecutionId = Guid.NewGuid(),
            WorkflowDefinitionKey = "test-workflow",
            WorkflowDefinitionVersion = 1,
            StepKey = "call-api",
            StepType = "HttpRequest",
            State = StateWithInputAndStep(
                """{"customerId":"cus_123"}""",
                "fetch-order",
                """{"order":{"id":"ord_789"}}"""),
            Secrets = new Dictionary<string, string>
            {
                ["api-key"] = "secret_123"
            }
        };

        var result = request.ResolveTemplate(
            "https://api.example.com/customers/{{input.customerId}}/orders/{{steps.fetch-order.output.order.id}}?key={{secrets.api-key}}",
            "URL");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "https://api.example.com/customers/cus_123/orders/ord_789?key=secret_123",
            result.Value);
    }

    [Fact]
    public void ResolveTemplate_MissingValueIncludesStepAndFieldContextInFailure()
    {
        var request = new StepExecutionRequest
        {
            WorkflowInstanceId = Guid.NewGuid(),
            StepExecutionId = Guid.NewGuid(),
            WorkflowDefinitionKey = "test-workflow",
            WorkflowDefinitionVersion = 1,
            StepKey = "call-api",
            StepType = "HttpRequest",
            State = StateWithInput("""{"customerId":"cus_123"}""")
        };

        var result = request.ResolveTemplate("{{steps.fetch-order.output.order.id}}", "URL");

        Assert.False(result.IsSuccess);
        Assert.Contains("call-api", result.Error);
        Assert.Contains("URL", result.Error);
        Assert.Contains("fetch-order", result.Error);
    }

    [Fact]
    public void ResolveTemplate_WithoutState_PreservesOriginalTemplate()
    {
        var request = new StepExecutionRequest
        {
            WorkflowInstanceId = Guid.NewGuid(),
            StepExecutionId = Guid.NewGuid(),
            WorkflowDefinitionKey = "legacy-workflow",
            WorkflowDefinitionVersion = 1,
            StepKey = "legacy-step"
        };

        var result = request.ResolveTemplate("{{input.customerId}}", "URL");

        Assert.True(result.IsSuccess);
        Assert.Equal("{{input.customerId}}", result.Value);
    }

    [Fact]
    public void ResolveValueReference_ResolvesTypedObjectFromPlaceholder()
    {
        var request = new StepExecutionRequest
        {
            WorkflowInstanceId = Guid.NewGuid(),
            StepExecutionId = Guid.NewGuid(),
            WorkflowDefinitionKey = "test-workflow",
            WorkflowDefinitionVersion = 1,
            StepKey = "map-data",
            StepType = "Transform",
            State = StateWithInputAndStep(
                """{"customerId":"cus_123"}""",
                "fetch-order",
                """{"body":{"order":{"id":"ord_789"}}}""")
        };

        var result = request.ResolveValueReference(
            "{{steps.fetch-order.output.body.order}}",
            "transform source");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(JsonValueKind.Object, result.Value!.Value.ValueKind);
        Assert.Equal("ord_789", result.Value.Value.GetProperty("id").GetString());
    }

    [Fact]
    public void ResolveValueReference_LegacyInputPath_ResolvesTypedValue()
    {
        var request = new StepExecutionRequest
        {
            WorkflowInstanceId = Guid.NewGuid(),
            StepExecutionId = Guid.NewGuid(),
            WorkflowDefinitionKey = "test-workflow",
            WorkflowDefinitionVersion = 1,
            StepKey = "map-data",
            StepType = "Transform",
            State = StateWithInput("""{"payload":{"customerId":"cus_123"}}""")
        };

        var result = request.ResolveValueReference("$.payload", "transform source");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(JsonValueKind.Object, result.Value!.Value.ValueKind);
        Assert.Equal("cus_123", result.Value.Value.GetProperty("customerId").GetString());
    }

    private static WorkflowState StateWithInput(string inputJson) =>
        new(
            new WorkflowStateMetadata(
                Guid.NewGuid(),
                "test-workflow",
                1,
                WorkflowInstanceStatus.Running,
                DateTimeOffset.UtcNow,
                null),
            triggerData: null,
            input: inputJson,
            steps: new Dictionary<string, WorkflowStepState>());

    private static WorkflowState StateWithInputAndStep(string inputJson, string stepKey, string outputJson) =>
        new(
            new WorkflowStateMetadata(
                Guid.NewGuid(),
                "test-workflow",
                1,
                WorkflowInstanceStatus.Running,
                DateTimeOffset.UtcNow,
                null),
            triggerData: null,
            input: inputJson,
            steps: new Dictionary<string, WorkflowStepState>
            {
                [stepKey] = new WorkflowStepState(
                    stepKey,
                    WorkflowStepExecutionStatus.Completed,
                    outputJson,
                    error: null,
                    attempts: [])
            });
}
