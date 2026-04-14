using StepTrail.Shared.Entities;

namespace StepTrail.Shared.Runtime;

/// <summary>
/// Assembles a WorkflowState from relational EF entities.
///
/// Call sites pass the workflow instance plus all of its step execution rows.
/// The assembler groups executions by step key, determines current step status,
/// and surfaces the output of the most recent successful attempt per step.
/// </summary>
public static class WorkflowStateAssembler
{
    public static WorkflowState Assemble(
        WorkflowInstance instance,
        IEnumerable<WorkflowStepExecution> executions)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(executions);

        var metadata = new WorkflowStateMetadata(
            instance.Id,
            instance.WorkflowDefinitionKey ?? string.Empty,
            instance.WorkflowDefinitionVersion ?? 0,
            instance.Status,
            instance.CreatedAt,
            instance.CompletedAt);

        var steps = executions
            .GroupBy(e => e.StepKey)
            .ToDictionary(
                g => g.Key,
                g => BuildStepState(g.Key, g));

        return new WorkflowState(
            metadata,
            triggerData: instance.TriggerData,
            input: instance.Input,
            steps: steps);
    }

    private static WorkflowStepState BuildStepState(
        string stepKey,
        IEnumerable<WorkflowStepExecution> executions)
    {
        // Sort by creation time only. Attempt numbers reset to 1 on manual retry
        // (WorkflowRetryService creates a new row with Attempt = 1), so sorting by
        // Attempt would place the new pending row before old higher-numbered rows,
        // making "latest" report a stale failed execution as current.
        var ordered = executions
            .OrderBy(e => e.CreatedAt)
            .ToList();

        var latest = ordered[^1];

        // Output comes from the most recently completed attempt across all runs.
        var successOutput = ordered
            .Where(e => e.Status == WorkflowStepExecutionStatus.Completed)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefault()
            ?.Output;

        var attempts = ordered  // already sorted by CreatedAt — chronological order for UI
            .Select(e => new WorkflowStepAttempt(
                e.Attempt,
                e.Status,
                e.Output,
                e.Error,
                e.StartedAt,
                e.CompletedAt))
            .ToList();

        return new WorkflowStepState(
            stepKey,
            status: latest.Status,
            output: successOutput,
            error: latest.Error,
            attempts: attempts);
    }
}
