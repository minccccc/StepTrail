using System.Text.Json.Serialization;

namespace StepTrail.Shared.Definitions;

public sealed class DelayStepConfiguration
{
    private DelayStepConfiguration()
    {
    }

    public DelayStepConfiguration(int delaySeconds)
    {
        if (delaySeconds < 1)
            throw new ArgumentOutOfRangeException(nameof(delaySeconds), "Delay step duration must be 1 second or greater.");

        DelaySeconds = delaySeconds;
    }

    public DelayStepConfiguration(string targetTimeExpression)
    {
        if (string.IsNullOrWhiteSpace(targetTimeExpression))
            throw new ArgumentException("Delay-until target time expression must not be empty.", nameof(targetTimeExpression));

        TargetTimeExpression = targetTimeExpression.Trim();
    }

    public int? DelaySeconds { get; private set; }
    public string? TargetTimeExpression { get; private set; }

    [JsonIgnore]
    public bool UsesFixedDelay => DelaySeconds.HasValue;

    [JsonIgnore]
    public bool UsesTargetTime => !string.IsNullOrWhiteSpace(TargetTimeExpression);
}
