using System.Text.Json;
using StepTrail.Shared.Entities;
using StepTrail.Shared.Runtime;
using StepTrail.Shared.Workflows;
using StepTrail.Worker.Handlers;
using Xunit;

namespace StepTrail.Shared.Tests.Runtime;

public class DelayStepExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_FirstExecution_ReturnsWaitingResultWithNormalizedOutput()
    {
        var executor = new DelayStepExecutor();
        var before = DateTimeOffset.UtcNow;

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "wait-before-follow-up",
                StepType = "Delay",
                StepConfiguration = """{"delaySeconds":30}"""
            },
            CancellationToken.None);

        var after = DateTimeOffset.UtcNow;

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.ResumeAtUtc);
        Assert.InRange(result.ResumeAtUtc!.Value, before.AddSeconds(30), after.AddSeconds(30));

        using var document = JsonDocument.Parse(result.Output!);
        Assert.Equal("fixed", document.RootElement.GetProperty("delayType").GetString());
        Assert.Equal("00:00:30", document.RootElement.GetProperty("requestedDuration").GetString());
        Assert.Equal(result.ResumeAtUtc.Value.UtcDateTime, document.RootElement.GetProperty("resumeAtUtc").GetDateTime().ToUniversalTime());
    }

    [Fact]
    public async Task ExecuteAsync_ResumedExecution_CompletesImmediatelyUsingPersistedOutput()
    {
        var executor = new DelayStepExecutor();

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "wait-before-follow-up",
                StepType = "Delay",
                StepConfiguration = """{"delaySeconds":30}""",
                CurrentOutput = """{"delayType":"fixed","requestedDuration":"00:00:30","resumeAtUtc":"2026-04-15T12:30:00Z"}"""
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.ResumeAtUtc);
        Assert.Equal("""{"delayType":"fixed","requestedDuration":"00:00:30","resumeAtUtc":"2026-04-15T12:30:00Z"}""", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidDuration_ReturnsInvalidConfiguration()
    {
        var executor = new DelayStepExecutor();

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "wait-before-follow-up",
                StepType = "Delay",
                StepConfiguration = """{"delaySeconds":0}"""
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(StepExecutionFailureClassification.InvalidConfiguration, result.Failure!.Classification);
        Assert.Contains("1 second or greater", result.Failure.Message);
    }

    [Fact]
    public async Task ExecuteAsync_BothDelayModesConfigured_ReturnsInvalidConfiguration()
    {
        var executor = new DelayStepExecutor();

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "wait-before-follow-up",
                StepType = "Delay",
                StepConfiguration = """{"delaySeconds":30,"targetTimeExpression":"2026-04-16T08:00:00Z"}"""
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(StepExecutionFailureClassification.InvalidConfiguration, result.Failure!.Classification);
        Assert.Contains("either delaySeconds or targetTimeExpression, but not both", result.Failure.Message);
    }

    [Fact]
    public async Task ExecuteAsync_DelayUntilLiteralFuture_ReturnsWaitingResult()
    {
        var executor = new DelayStepExecutor();
        var targetTimeUtc = DateTimeOffset.UtcNow.AddMinutes(5);

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "wait-until-follow-up",
                StepType = "Delay",
                StepConfiguration = $$"""{"targetTimeExpression":"{{targetTimeUtc:O}}"}"""
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.ResumeAtUtc);
        Assert.InRange(result.ResumeAtUtc!.Value, targetTimeUtc.AddSeconds(-1), targetTimeUtc.AddSeconds(1));

        using var document = JsonDocument.Parse(result.Output!);
        Assert.Equal("until", document.RootElement.GetProperty("delayType").GetString());
        Assert.Equal(targetTimeUtc.UtcDateTime, document.RootElement.GetProperty("targetTimeUtc").GetDateTime().ToUniversalTime(), TimeSpan.FromSeconds(1));
        Assert.False(document.RootElement.TryGetProperty("wasImmediate", out _));
    }

    [Fact]
    public async Task ExecuteAsync_DelayUntilPastTime_CompletesImmediately()
    {
        var executor = new DelayStepExecutor();
        var targetTimeUtc = DateTimeOffset.UtcNow.AddMinutes(-1);

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "wait-until-follow-up",
                StepType = "Delay",
                StepConfiguration = $$"""{"targetTimeExpression":"{{targetTimeUtc:O}}"}"""
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.ResumeAtUtc);

        using var document = JsonDocument.Parse(result.Output!);
        Assert.Equal("until", document.RootElement.GetProperty("delayType").GetString());
        Assert.True(document.RootElement.GetProperty("wasImmediate").GetBoolean());
        Assert.Equal(targetTimeUtc.UtcDateTime, document.RootElement.GetProperty("targetTimeUtc").GetDateTime().ToUniversalTime(), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ExecuteAsync_DelayUntilPlaceholderFuture_ReturnsWaitingResult()
    {
        var executor = new DelayStepExecutor();
        var targetTimeUtc = DateTimeOffset.UtcNow.AddMinutes(10);

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "wait-until-follow-up",
                StepType = "Delay",
                StepConfiguration = """{"targetTimeExpression":"{{input.followUpAtUtc}}"}""",
                State = StateWithInput($$"""{"followUpAtUtc":"{{targetTimeUtc:O}}"}""")
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.ResumeAtUtc);
        Assert.InRange(result.ResumeAtUtc!.Value, targetTimeUtc.AddSeconds(-1), targetTimeUtc.AddSeconds(1));
    }

    [Fact]
    public async Task ExecuteAsync_DelayUntilPlaceholderInvalidTimestamp_ReturnsInputResolutionFailure()
    {
        var executor = new DelayStepExecutor();

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "wait-until-follow-up",
                StepType = "Delay",
                StepConfiguration = """{"targetTimeExpression":"{{input.followUpAtUtc}}"}""",
                State = StateWithInput("""{"followUpAtUtc":"tomorrow morning"}""")
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(StepExecutionFailureClassification.InputResolutionFailure, result.Failure!.Classification);
        Assert.Contains("not a valid UTC timestamp", result.Failure.Message);
    }

    [Fact]
    public async Task ExecuteAsync_DelayUntilLiteralInvalidTimestamp_ReturnsInvalidConfiguration()
    {
        var executor = new DelayStepExecutor();

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "wait-until-follow-up",
                StepType = "Delay",
                StepConfiguration = """{"targetTimeExpression":"tomorrow morning"}"""
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(StepExecutionFailureClassification.InvalidConfiguration, result.Failure!.Classification);
        Assert.Contains("not a valid UTC timestamp", result.Failure.Message);
    }

    [Fact]
    public async Task ExecuteAsync_DelayUntilLiteralWithoutUtcDesignator_ReturnsInvalidConfiguration()
    {
        var executor = new DelayStepExecutor();

        var result = await executor.ExecuteAsync(
            new StepExecutionRequest
            {
                WorkflowInstanceId = Guid.NewGuid(),
                StepExecutionId = Guid.NewGuid(),
                WorkflowDefinitionKey = "customer-sync",
                WorkflowDefinitionVersion = 1,
                StepKey = "wait-until-follow-up",
                StepType = "Delay",
                StepConfiguration = """{"targetTimeExpression":"2026-04-16T08:00:00"}"""
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(StepExecutionFailureClassification.InvalidConfiguration, result.Failure!.Classification);
        Assert.Contains("not a valid UTC timestamp", result.Failure.Message);
    }

    private static WorkflowState StateWithInput(string inputJson) =>
        new(
            new WorkflowStateMetadata(
                Guid.NewGuid(),
                "customer-sync",
                1,
                WorkflowInstanceStatus.Running,
                DateTimeOffset.UtcNow,
                null),
            triggerData: null,
            input: inputJson,
            steps: new Dictionary<string, WorkflowStepState>());
}
