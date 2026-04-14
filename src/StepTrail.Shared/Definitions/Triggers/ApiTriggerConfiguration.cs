namespace StepTrail.Shared.Definitions;

public sealed class ApiTriggerConfiguration
{
    private ApiTriggerConfiguration()
    {
        OperationKey = string.Empty;
    }

    public ApiTriggerConfiguration(string operationKey)
    {
        if (string.IsNullOrWhiteSpace(operationKey))
            throw new ArgumentException("API trigger operation key must not be empty.", nameof(operationKey));

        OperationKey = operationKey.Trim();
    }

    public string OperationKey { get; private set; }
}
