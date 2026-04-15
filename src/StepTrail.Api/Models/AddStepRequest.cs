namespace StepTrail.Api.Models;

public sealed class AddStepRequest
{
    public string StepKey { get; init; } = string.Empty;
    public string StepType { get; init; } = string.Empty;
}
