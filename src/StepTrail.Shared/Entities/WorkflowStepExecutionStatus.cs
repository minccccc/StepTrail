namespace StepTrail.Shared.Entities;

public enum WorkflowStepExecutionStatus
{
    NotStarted,
    Pending,
    Waiting,
    Running,
    Completed,
    Failed,
    Cancelled
}
