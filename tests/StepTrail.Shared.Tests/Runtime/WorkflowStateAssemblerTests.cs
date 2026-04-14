using StepTrail.Shared.Entities;
using StepTrail.Shared.Runtime;
using Xunit;

namespace StepTrail.Shared.Tests.Runtime;

public class WorkflowStateAssemblerTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static readonly DateTimeOffset T0 = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static WorkflowInstance MakeInstance() => new()
    {
        Id                      = Guid.NewGuid(),
        WorkflowDefinitionKey   = "test-workflow",
        WorkflowDefinitionVersion = 1,
        Status                  = WorkflowInstanceStatus.Running,
        Input                   = """{"orderId":"123"}""",
        CreatedAt               = T0,
        UpdatedAt               = T0
    };

    private static WorkflowStepExecution MakeExecution(
        Guid instanceId,
        string stepKey,
        int attempt,
        WorkflowStepExecutionStatus status,
        DateTimeOffset createdAt,
        string? output = null,
        string? error  = null) => new()
    {
        Id                  = Guid.NewGuid(),
        WorkflowInstanceId  = instanceId,
        StepKey             = stepKey,
        Attempt             = attempt,
        Status              = status,
        Output              = output,
        Error               = error,
        ScheduledAt         = createdAt,
        CreatedAt           = createdAt,
        UpdatedAt           = createdAt
    };

    // ── Basic single-run cases ────────────────────────────────────────────────

    [Fact]
    public void Assemble_SingleCompletedStep_ReturnsOutput()
    {
        var instance = MakeInstance();
        var exec = MakeExecution(
            instance.Id, "fetch", attempt: 1,
            WorkflowStepExecutionStatus.Completed,
            T0, output: """{"statusCode":200}""");

        var state = WorkflowStateAssembler.Assemble(instance, [exec]);

        var step = Assert.Single(state.Steps.Values);
        Assert.Equal(WorkflowStepExecutionStatus.Completed, step.Status);
        Assert.Equal("""{"statusCode":200}""", step.Output);
    }

    [Fact]
    public void Assemble_MultipleAttempts_FirstAttemptFailsSecondSucceeds_ReturnsSuccessOutput()
    {
        var instance = MakeInstance();
        var fail = MakeExecution(
            instance.Id, "call", attempt: 1,
            WorkflowStepExecutionStatus.Failed,
            T0, error: "timeout");
        var pass = MakeExecution(
            instance.Id, "call", attempt: 2,
            WorkflowStepExecutionStatus.Completed,
            T0.AddSeconds(30), output: """{"statusCode":200}""");

        var state = WorkflowStateAssembler.Assemble(instance, [fail, pass]);

        var step = state.Steps["call"];
        Assert.Equal(WorkflowStepExecutionStatus.Completed, step.Status);
        Assert.Equal("""{"statusCode":200}""", step.Output);
    }

    // ── Retry (Attempt resets to 1) ───────────────────────────────────────────

    [Fact]
    public void Assemble_AfterRetry_LatestStatusIsNewPendingNotOldFailed()
    {
        // After a manual retry WorkflowRetryService creates a new row with Attempt = 1,
        // while the old failed chain ended at a higher attempt number.
        // The assembler must use CreatedAt order so the newer row is treated as current.
        var instance = MakeInstance();

        // Original run: attempt 1 → 2 → 3 (all failed)
        var run1a1 = MakeExecution(instance.Id, "call", attempt: 1, WorkflowStepExecutionStatus.Failed, T0,               error: "err1");
        var run1a2 = MakeExecution(instance.Id, "call", attempt: 2, WorkflowStepExecutionStatus.Failed, T0.AddSeconds(10), error: "err2");
        var run1a3 = MakeExecution(instance.Id, "call", attempt: 3, WorkflowStepExecutionStatus.Failed, T0.AddSeconds(20), error: "err3");

        // Manual retry: fresh row with Attempt = 1, created after all previous rows
        var retryRow = MakeExecution(instance.Id, "call", attempt: 1, WorkflowStepExecutionStatus.Pending, T0.AddSeconds(60));

        var state = WorkflowStateAssembler.Assemble(instance, [run1a1, run1a2, run1a3, retryRow]);

        var step = state.Steps["call"];
        // "latest" must be the retry row, not the old Attempt=3 failed row
        Assert.Equal(WorkflowStepExecutionStatus.Pending, step.Status);
        Assert.Null(step.Error);    // new pending row has no error
        Assert.Null(step.Output);   // no successful attempt yet
    }

    [Fact]
    public void Assemble_AfterRetry_AttemptsListInChronologicalOrder()
    {
        var instance = MakeInstance();
        var a1 = MakeExecution(instance.Id, "call", attempt: 1, WorkflowStepExecutionStatus.Failed,  T0,                error: "err1");
        var a2 = MakeExecution(instance.Id, "call", attempt: 2, WorkflowStepExecutionStatus.Failed,  T0.AddSeconds(10), error: "err2");
        var retry = MakeExecution(instance.Id, "call", attempt: 1, WorkflowStepExecutionStatus.Pending, T0.AddSeconds(60));

        var state = WorkflowStateAssembler.Assemble(instance, [a1, a2, retry]);

        var attempts = state.Steps["call"].Attempts;
        Assert.Equal(3, attempts.Count);
        // First two are from original run (older timestamps), third is the retry row
        Assert.Equal(WorkflowStepExecutionStatus.Failed,  attempts[0].Status);
        Assert.Equal(WorkflowStepExecutionStatus.Failed,  attempts[1].Status);
        Assert.Equal(WorkflowStepExecutionStatus.Pending, attempts[2].Status);
    }

    // ── Replay (new full chain of rows, old rows preserved) ───────────────────

    [Fact]
    public void Assemble_AfterReplay_LatestStatusIsNewPendingFromReplayChain()
    {
        var instance = MakeInstance();

        // Old completed run
        var oldRun = MakeExecution(
            instance.Id, "fetch", attempt: 1,
            WorkflowStepExecutionStatus.Completed,
            T0, output: """{"statusCode":200}""");

        // Replay creates a new Pending row with a newer CreatedAt
        var replayRow = MakeExecution(
            instance.Id, "fetch", attempt: 1,
            WorkflowStepExecutionStatus.Pending,
            T0.AddMinutes(5));

        var state = WorkflowStateAssembler.Assemble(instance, [oldRun, replayRow]);

        var step = state.Steps["fetch"];
        // "latest" should be the replay row, not the old completed row
        Assert.Equal(WorkflowStepExecutionStatus.Pending, step.Status);
        // But the most-recently-completed output is still from the old run
        Assert.Equal("""{"statusCode":200}""", step.Output);
    }
}
