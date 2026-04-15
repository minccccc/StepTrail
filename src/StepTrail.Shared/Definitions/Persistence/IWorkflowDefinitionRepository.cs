namespace StepTrail.Shared.Definitions;

public interface IWorkflowDefinitionRepository
{
    Task<WorkflowDefinition> CreateDraftAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default);
    Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkflowDefinition?> GetByKeyAndVersionAsync(string key, int version, CancellationToken cancellationToken = default);
    Task<WorkflowDefinition?> GetActiveByKeyAsync(string key, CancellationToken cancellationToken = default);
    Task<WorkflowDefinition?> GetActiveWebhookByRouteKeyAsync(string routeKey, CancellationToken cancellationToken = default);
    Task<WorkflowDefinition> UpdateAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default);
    Task<WorkflowDefinition> SaveNewVersionAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default);
}
