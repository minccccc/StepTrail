namespace StepTrail.Shared.Entities;

public enum WorkflowInstanceStatus
{
    Pending,
    Running,

    /// <summary>
    /// A step failed with a retryable classification and a retry attempt has been scheduled.
    /// The workflow is not terminally failed — it will return to Running when the retry is claimed.
    /// </summary>
    AwaitingRetry,

    Completed,
    Failed,
    Cancelled,
    Archived
}
