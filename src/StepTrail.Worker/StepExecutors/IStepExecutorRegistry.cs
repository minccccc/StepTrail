using StepTrail.Shared.Definitions;

namespace StepTrail.Worker.StepExecutors;

public interface IStepExecutorRegistry
{
    ResolvedStepExecutor Resolve(StepType stepType, string? stepConfiguration);
}
