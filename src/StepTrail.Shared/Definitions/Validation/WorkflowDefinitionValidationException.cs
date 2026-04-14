namespace StepTrail.Shared.Definitions;

public sealed class WorkflowDefinitionValidationException : Exception
{
    public WorkflowDefinitionValidationException(WorkflowDefinitionValidationResult validationResult)
        : base(BuildMessage(validationResult))
    {
        ValidationResult = validationResult ?? throw new ArgumentNullException(nameof(validationResult));
    }

    public WorkflowDefinitionValidationResult ValidationResult { get; }

    private static string BuildMessage(WorkflowDefinitionValidationResult validationResult)
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        if (validationResult.IsValid)
            return "Workflow definition activation validation did not produce any errors.";

        if (validationResult.Errors.Count == 1)
            return $"Workflow definition activation validation failed: {validationResult.Errors[0].Message}";

        return $"Workflow definition activation validation failed with {validationResult.Errors.Count} errors.";
    }
}
