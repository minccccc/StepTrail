using StepTrail.Shared.Definitions;

namespace StepTrail.Shared.Runtime.Scheduling;

public static class ScheduleTriggerTimingCalculator
{
    public static DateTimeOffset GetInitialNextRunAt(
        ScheduleTriggerConfiguration configuration,
        DateTimeOffset activatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.IntervalSeconds.HasValue)
            return activatedAtUtc;

        return GetNextCronOccurrence(configuration.CronExpression!, activatedAtUtc);
    }

    public static DateTimeOffset GetNextRunAtAfterExecution(
        ScheduleTriggerConfiguration configuration,
        DateTimeOffset executedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.IntervalSeconds.HasValue)
            return executedAtUtc.AddSeconds(configuration.IntervalSeconds.Value);

        return GetNextCronOccurrence(configuration.CronExpression!, executedAtUtc);
    }

    public static DateTimeOffset GetResynchronizedNextRunAt(
        ScheduleTriggerConfiguration configuration,
        DateTimeOffset? lastRunAtUtc,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.IntervalSeconds.HasValue)
        {
            if (!lastRunAtUtc.HasValue)
                return nowUtc;

            var candidate = lastRunAtUtc.Value.AddSeconds(configuration.IntervalSeconds.Value);
            return candidate > nowUtc ? candidate : nowUtc;
        }

        return GetNextCronOccurrence(configuration.CronExpression!, nowUtc);
    }

    private static DateTimeOffset GetNextCronOccurrence(string cronExpression, DateTimeOffset afterUtc)
    {
        if (!SimpleCronExpression.TryParse(cronExpression, out var parsedExpression, out var error))
        {
            throw new InvalidOperationException(
                $"Schedule trigger cron expression '{cronExpression}' is invalid: {error}");
        }

        var nextOccurrence = parsedExpression!.GetNextOccurrence(afterUtc);
        if (!nextOccurrence.HasValue)
        {
            throw new InvalidOperationException(
                $"Schedule trigger cron expression '{cronExpression}' does not produce a future execution time.");
        }

        return nextOccurrence.Value;
    }
}
