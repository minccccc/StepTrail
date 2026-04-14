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

    public int DelaySeconds { get; private set; }
}
