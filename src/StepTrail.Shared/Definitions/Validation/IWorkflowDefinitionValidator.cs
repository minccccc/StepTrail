namespace StepTrail.Shared.Definitions;

public interface IWorkflowDefinitionValidator
{
    WorkflowDefinitionValidationResult ValidateForActivation(WorkflowDefinition definition);
}
