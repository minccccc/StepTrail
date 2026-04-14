namespace StepTrail.Shared.Definitions;

public interface IWorkflowDefinitionActivationGuard
{
    void EnsureCanActivate(WorkflowDefinition definition);
}
