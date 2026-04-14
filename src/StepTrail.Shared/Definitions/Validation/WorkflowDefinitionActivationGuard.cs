namespace StepTrail.Shared.Definitions;

public sealed class WorkflowDefinitionActivationGuard : IWorkflowDefinitionActivationGuard
{
    private readonly IWorkflowDefinitionValidator _workflowDefinitionValidator;

    public WorkflowDefinitionActivationGuard(IWorkflowDefinitionValidator workflowDefinitionValidator)
    {
        _workflowDefinitionValidator = workflowDefinitionValidator;
    }

    public void EnsureCanActivate(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var validationResult = _workflowDefinitionValidator.ValidateForActivation(definition);
        if (!validationResult.IsValid)
            throw new WorkflowDefinitionValidationException(validationResult);
    }
}
