namespace StepTrail.Shared.Definitions;

public sealed record WorkflowDefinitionValidationError(
    string Code,
    string Path,
    string Message);
