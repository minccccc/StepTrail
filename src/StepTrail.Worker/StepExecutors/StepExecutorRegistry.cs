using StepTrail.Shared.Definitions;

namespace StepTrail.Worker.StepExecutors;

public sealed class StepExecutorRegistry : IStepExecutorRegistry
{
    private readonly IReadOnlyDictionary<StepType, StepExecutorRegistration> _registrations;

    public StepExecutorRegistry(IEnumerable<StepExecutorRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        var materializedRegistrations = registrations.ToList();
        var duplicateStepTypes = materializedRegistrations
            .GroupBy(registration => registration.StepType)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.ToString())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (duplicateStepTypes.Count > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate step executor registrations were found for step types: {string.Join(", ", duplicateStepTypes)}.");
        }

        _registrations = materializedRegistrations.ToDictionary(
            registration => registration.StepType,
            registration => registration);
    }

    public ResolvedStepExecutor Resolve(StepType stepType, string? stepConfiguration)
    {
        if (!_registrations.TryGetValue(stepType, out var registration))
        {
            throw new InvalidOperationException(
                $"No step executor registration exists for executable step type '{stepType}'.");
        }

        return new ResolvedStepExecutor(
            stepType,
            registration.ExecutorKey,
            registration.NormalizeConfiguration(stepConfiguration));
    }

}
