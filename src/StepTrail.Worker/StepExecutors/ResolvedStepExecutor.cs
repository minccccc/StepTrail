using StepTrail.Shared.Definitions;

namespace StepTrail.Worker.StepExecutors;

public sealed record ResolvedStepExecutor(
    StepType StepType,
    string ExecutorKey,
    string? StepConfiguration);
