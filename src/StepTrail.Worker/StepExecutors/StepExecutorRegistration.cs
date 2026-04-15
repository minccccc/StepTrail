using StepTrail.Shared.Definitions;

namespace StepTrail.Worker.StepExecutors;

public sealed class StepExecutorRegistration
{
    public StepExecutorRegistration(
        StepType stepType,
        string executorKey,
        Func<string?, string?>? normalizeConfiguration = null)
    {
        if (string.IsNullOrWhiteSpace(executorKey))
            throw new ArgumentException("Step executor key must not be empty.", nameof(executorKey));

        StepType = stepType;
        ExecutorKey = executorKey.Trim();
        NormalizeConfiguration = normalizeConfiguration ?? (configuration => configuration);
    }

    public StepType StepType { get; }
    public string ExecutorKey { get; }
    public Func<string?, string?> NormalizeConfiguration { get; }
}
