namespace StepTrail.Shared.Definitions;

public sealed class ScheduleTriggerConfiguration
{
    private ScheduleTriggerConfiguration()
    {
    }

    public ScheduleTriggerConfiguration(int intervalSeconds)
    {
        if (intervalSeconds < 1)
            throw new ArgumentOutOfRangeException(nameof(intervalSeconds), "Schedule trigger interval must be 1 second or greater.");

        IntervalSeconds = intervalSeconds;
    }

    public int IntervalSeconds { get; private set; }
}
