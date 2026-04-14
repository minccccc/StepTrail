namespace StepTrail.Shared.Definitions;

public sealed class WorkflowDefinitionValidationResult
{
    private readonly List<WorkflowDefinitionValidationError> _errors = [];

    public bool IsValid => _errors.Count == 0;
    public IReadOnlyList<WorkflowDefinitionValidationError> Errors => _errors;

    public void AddError(string code, string path, string message)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Validation error code must not be empty.", nameof(code));
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Validation error path must not be empty.", nameof(path));
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Validation error message must not be empty.", nameof(message));

        _errors.Add(new WorkflowDefinitionValidationError(code.Trim(), path.Trim(), message.Trim()));
    }
}
