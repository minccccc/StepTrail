namespace StepTrail.Shared.Workflows;

/// <summary>
/// Structured outcome returned by a step executor.
/// Expected business/runtime failures should be returned here rather than thrown.
/// Exceptions are reserved for truly unexpected conditions.
/// </summary>
public sealed class StepExecutionResult
{
    private StepExecutionResult(
        StepExecutionOutcome outcome,
        string? output,
        StepExecutionFailure? failure,
        StepExecutionContinuation continuation,
        DateTimeOffset? resumeAtUtc)
    {
        if (outcome == StepExecutionOutcome.Succeeded && failure is not null)
            throw new ArgumentException("Successful step execution result must not include a failure.", nameof(failure));

        if (outcome == StepExecutionOutcome.Failed && failure is null)
            throw new ArgumentException("Failed step execution result must include a failure.", nameof(failure));

        if (outcome == StepExecutionOutcome.Failed && continuation != StepExecutionContinuation.ContinueWorkflow)
            throw new ArgumentException("Failed step execution results cannot specify a workflow continuation override.", nameof(continuation));

        if (resumeAtUtc.HasValue && outcome != StepExecutionOutcome.Succeeded)
            throw new ArgumentException("Only successful step execution results can schedule a future resume.", nameof(resumeAtUtc));

        if (resumeAtUtc.HasValue && continuation != StepExecutionContinuation.ContinueWorkflow)
            throw new ArgumentException("Paused step execution results cannot specify a workflow continuation override.", nameof(resumeAtUtc));

        Outcome = outcome;
        Output = output;
        Failure = failure;
        Continuation = continuation;
        ResumeAtUtc = resumeAtUtc;
    }

    public StepExecutionOutcome Outcome { get; }
    public string? Output { get; }
    public StepExecutionFailure? Failure { get; }
    public StepExecutionContinuation Continuation { get; }
    public DateTimeOffset? ResumeAtUtc { get; }
    public bool IsSuccess => Outcome == StepExecutionOutcome.Succeeded;

    public static StepExecutionResult Success(string? output = null) =>
        new(StepExecutionOutcome.Succeeded, output, null, StepExecutionContinuation.ContinueWorkflow, null);

    public static StepExecutionResult CompleteWorkflow(string? output = null) =>
        new(StepExecutionOutcome.Succeeded, output, null, StepExecutionContinuation.CompleteWorkflow, null);

    public static StepExecutionResult CancelWorkflow(string? output = null) =>
        new(StepExecutionOutcome.Succeeded, output, null, StepExecutionContinuation.CancelWorkflow, null);

    public static StepExecutionResult WaitUntil(DateTimeOffset resumeAtUtc, string? output = null)
    {
        if (resumeAtUtc == default)
            throw new ArgumentOutOfRangeException(nameof(resumeAtUtc), "Resume time must not be the default timestamp.");

        return new StepExecutionResult(
            StepExecutionOutcome.Succeeded,
            output,
            null,
            StepExecutionContinuation.ContinueWorkflow,
            resumeAtUtc);
    }

    public static StepExecutionResult TransientFailure(
        string message,
        string? output = null,
        string? details = null,
        TimeSpan? retryDelayHint = null) =>
        FailureResult(StepExecutionFailureClassification.TransientFailure, message, output, details, retryDelayHint);

    public static StepExecutionResult PermanentFailure(
        string message,
        string? output = null,
        string? details = null) =>
        FailureResult(StepExecutionFailureClassification.PermanentFailure, message, output, details, null);

    public static StepExecutionResult InvalidConfiguration(
        string message,
        string? output = null,
        string? details = null) =>
        FailureResult(StepExecutionFailureClassification.InvalidConfiguration, message, output, details, null);

    public static StepExecutionResult InputResolutionFailure(
        string message,
        string? output = null,
        string? details = null) =>
        FailureResult(StepExecutionFailureClassification.InputResolutionFailure, message, output, details, null);

    private static StepExecutionResult FailureResult(
        StepExecutionFailureClassification classification,
        string message,
        string? output,
        string? details,
        TimeSpan? retryDelayHint)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Step execution failure message must not be empty.", nameof(message));

        return new StepExecutionResult(
            StepExecutionOutcome.Failed,
            output,
            new StepExecutionFailure
            {
                Classification = classification,
                Message = message.Trim(),
                Details = string.IsNullOrWhiteSpace(details) ? null : details.Trim(),
                RetryDelayHint = retryDelayHint
            },
            StepExecutionContinuation.ContinueWorkflow,
            null);
    }
}
