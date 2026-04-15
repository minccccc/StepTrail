using System.Text.Json.Serialization;
using StepTrail.Shared.Runtime.Scheduling;

namespace StepTrail.Shared.Definitions;

public sealed class ScheduleTriggerConfiguration
{
    private ScheduleTriggerConfiguration()
    {
    }

    public ScheduleTriggerConfiguration(int intervalSeconds)
    {
        if (intervalSeconds < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalSeconds),
                "Schedule trigger interval must be 1 second or greater.");
        }

        IntervalSeconds = intervalSeconds;
        CronExpression = null;
    }

    public ScheduleTriggerConfiguration(string cronExpression)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ArgumentException("Schedule trigger cron expression must not be empty.", nameof(cronExpression));

        if (!SimpleCronExpression.TryParse(cronExpression, out _, out var error))
        {
            throw new ArgumentException(
                $"Schedule trigger cron expression is invalid: {error}",
                nameof(cronExpression));
        }

        IntervalSeconds = null;
        CronExpression = cronExpression.Trim();
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? IntervalSeconds { get; private set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CronExpression { get; private set; }
}
