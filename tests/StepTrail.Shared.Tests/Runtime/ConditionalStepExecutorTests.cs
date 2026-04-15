using System.Text.Json;
using StepTrail.Shared.Entities;
using StepTrail.Shared.Runtime;
using StepTrail.Shared.Workflows;
using StepTrail.Worker.Handlers;
using Xunit;

namespace StepTrail.Shared.Tests.Runtime;

public class ConditionalStepExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_MatchedCondition_ReturnsSuccessAndContinuesWorkflow()
    {
        var executor = new ConditionalStepExecutor();

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "check-status",
                StepType = "Conditional",
                StepConfiguration =
                    """
                    {
                      "sourcePath": "$.payload.status",
                      "operator": 1,
                      "expectedValue": "paid",
                      "falseOutcome": 1
                    }
                    """,
                State = StateWithInput("""{"payload":{"status":"paid"}}""")
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(StepExecutionContinuation.ContinueWorkflow, result.Continuation);

        using var document = JsonDocument.Parse(result.Output!);
        Assert.True(document.RootElement.GetProperty("matched").GetBoolean());
        Assert.Equal("paid", document.RootElement.GetProperty("actualValue").GetString());
        Assert.Equal("paid", document.RootElement.GetProperty("expectedValue").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_NonMatchedCondition_ReturnsCompleteWorkflowContinuation()
    {
        var executor = new ConditionalStepExecutor();

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "check-status",
                StepType = "Conditional",
                StepConfiguration =
                    """
                    {
                      "conditionExpression": "payload.status == 'paid'",
                      "falseOutcome": 1
                    }
                    """,
                State = StateWithInput("""{"payload":{"status":"pending"}}""")
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(StepExecutionContinuation.CompleteWorkflow, result.Continuation);

        using var document = JsonDocument.Parse(result.Output!);
        Assert.False(document.RootElement.GetProperty("matched").GetBoolean());
        Assert.Equal("pending", document.RootElement.GetProperty("actualValue").GetString());
        Assert.Equal("paid", document.RootElement.GetProperty("expectedValue").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ExistsCondition_WhenValueMissing_ReturnsCancelWorkflowContinuation()
    {
        var executor = new ConditionalStepExecutor();

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "check-status",
                StepType = "Conditional",
                StepConfiguration =
                    """
                    {
                      "sourcePath": "$.payload.reviewedAt",
                      "operator": 3,
                      "falseOutcome": 2
                    }
                    """,
                State = StateWithInput("""{"payload":{"status":"pending"}}""")
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(StepExecutionContinuation.CancelWorkflow, result.Continuation);

        using var document = JsonDocument.Parse(result.Output!);
        Assert.False(document.RootElement.GetProperty("matched").GetBoolean());
        Assert.Equal("CancelWorkflow", document.RootElement.GetProperty("falseOutcome").GetString());
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
}
