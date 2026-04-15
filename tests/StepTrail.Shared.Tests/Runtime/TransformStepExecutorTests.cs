using System.Text.Json;
using StepTrail.Shared.Entities;
using StepTrail.Shared.Runtime;
using StepTrail.Shared.Workflows;
using StepTrail.Worker.Handlers;
using Xunit;

namespace StepTrail.Shared.Tests.Runtime;

public class TransformStepExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_BuildsStructuredOutputFromInputAndPriorStepOutput()
    {
        var executor = new TransformStepExecutor();

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "map-data",
                StepType = "Transform",
                StepConfiguration =
                    """
                    {
                      "mappings": [
                        { "targetPath": "$.request.customerId", "sourcePath": "$.customer.id" },
                        { "targetPath": "$.request.order", "sourcePath": "{{steps.fetch-order.output.body}}" },
                        { "targetPath": "snapshot", "sourcePath": "$.payload" },
                        { "targetPath": "origin", "sourcePath": "{{steps.fetch-order.output.source}}" }
                      ]
                    }
                    """,
                State = StateWithInputAndStep(
                    """{"customer":{"id":"cus_123"},"payload":{"source":"manual","amount":49}}""",
                    "fetch-order",
                    """{"body":{"orderId":"ord_789","status":"paid"},"source":"downstream"}""")
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Output);

        using var document = JsonDocument.Parse(result.Output!);
        var root = document.RootElement;

        Assert.Equal("cus_123", root.GetProperty("request").GetProperty("customerId").GetString());
        Assert.Equal("ord_789", root.GetProperty("request").GetProperty("order").GetProperty("orderId").GetString());
        Assert.Equal("paid", root.GetProperty("request").GetProperty("order").GetProperty("status").GetString());
        Assert.Equal("manual", root.GetProperty("snapshot").GetProperty("source").GetString());
        Assert.Equal(49, root.GetProperty("snapshot").GetProperty("amount").GetInt32());
        Assert.Equal("downstream", root.GetProperty("origin").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_MissingSourcePath_ReturnsInputResolutionFailure()
    {
        var executor = new TransformStepExecutor();

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "map-data",
                StepType = "Transform",
                StepConfiguration =
                    """
                    {
                      "mappings": [
                        { "targetPath": "customerId", "sourcePath": "$.customer.id" }
                      ]
                    }
                    """,
                State = StateWithInput("""{"payload":{"source":"manual"}}""")
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(StepExecutionFailureClassification.InputResolutionFailure, result.Failure!.Classification);
        Assert.Contains("customer.id", result.Failure.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ConflictingTargetPaths_ReturnsInvalidConfiguration()
    {
        var executor = new TransformStepExecutor();

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "map-data",
                StepType = "Transform",
                StepConfiguration =
                    """
                    {
                      "mappings": [
                        { "targetPath": "status", "sourcePath": "$.status" },
                        { "targetPath": "status.code", "sourcePath": "$.code" }
                      ]
                    }
                    """,
                State = StateWithInput("""{"status":"ready","code":"paid"}""")
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(StepExecutionFailureClassification.InvalidConfiguration, result.Failure!.Classification);
        Assert.Contains("status.code", result.Failure.Message);
    }

    [Fact]
    public async Task ExecuteAsync_DefaultValueOperation_UsesFallbackWhenSourceIsMissing()
    {
        var executor = new TransformStepExecutor();

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "map-data",
                StepType = "Transform",
                StepConfiguration =
                    """
                    {
                      "mappings": [
                        {
                          "targetPath": "customerId",
                          "operation": {
                            "type": 1,
                            "sourcePath": "$.customer.id",
                            "defaultValue": "unknown-customer"
                          }
                        }
                      ]
                    }
                    """,
                State = StateWithInput("""{"payload":{"source":"manual"}}""")
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        using var document = JsonDocument.Parse(result.Output!);
        Assert.Equal("unknown-customer", document.RootElement.GetProperty("customerId").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ConcatenateOperation_BuildsStringFromLiteralsAndResolvedValues()
    {
        var executor = new TransformStepExecutor();

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "map-data",
                StepType = "Transform",
                StepConfiguration =
                    """
                    {
                      "mappings": [
                        {
                          "targetPath": "reference",
                          "operation": {
                            "type": 2,
                            "parts": ["ORD-", "{{input.customerId}}", "-", "$.payload.sequence"]
                          }
                        }
                      ]
                    }
                    """,
                State = StateWithInput("""{"customerId":"cus_123","payload":{"sequence":7}}""")
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        using var document = JsonDocument.Parse(result.Output!);
        Assert.Equal("ORD-cus_123-7", document.RootElement.GetProperty("reference").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_FormatStringOperation_BuildsFormattedOutput()
    {
        var executor = new TransformStepExecutor();

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "map-data",
                StepType = "Transform",
                StepConfiguration =
                    """
                    {
                      "mappings": [
                        {
                          "targetPath": "summary",
                          "operation": {
                            "type": 3,
                            "template": "Customer {0} total {1}",
                            "arguments": ["{{input.customerId}}", "$.payload.amount"]
                          }
                        }
                      ]
                    }
                    """,
                State = StateWithInput("""{"customerId":"cus_123","payload":{"amount":49}}""")
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        using var document = JsonDocument.Parse(result.Output!);
        Assert.Equal("Customer cus_123 total 49", document.RootElement.GetProperty("summary").GetString());
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
