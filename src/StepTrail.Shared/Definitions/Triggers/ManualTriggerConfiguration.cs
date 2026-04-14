namespace StepTrail.Shared.Definitions;

public sealed class ManualTriggerConfiguration
{
    private ManualTriggerConfiguration()
    {
        EntryPointKey = string.Empty;
    }

    public ManualTriggerConfiguration(string entryPointKey)
    {
        if (string.IsNullOrWhiteSpace(entryPointKey))
            throw new ArgumentException("Manual trigger entry point key must not be empty.", nameof(entryPointKey));

        EntryPointKey = entryPointKey.Trim();
    }

    public string EntryPointKey { get; private set; }
}
