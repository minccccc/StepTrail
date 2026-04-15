namespace StepTrail.Shared.Workflows;

/// <summary>
/// Implemented by every step executor that the worker can run.
/// Executors receive a shared execution request shape and return
/// a structured success/failure result instead of using exceptions
/// for expected step outcomes.
/// </summary>
public interface IStepExecutor
{
    Task<StepExecutionResult> ExecuteAsync(StepExecutionRequest request, CancellationToken ct);
}
