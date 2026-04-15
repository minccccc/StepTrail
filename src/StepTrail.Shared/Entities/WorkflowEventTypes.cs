namespace StepTrail.Shared.Entities;

public static class WorkflowEventTypes
{
    public const string WorkflowStarted   = "WorkflowStarted";
    public const string StepStarted       = "StepStarted";
    public const string StepWaiting       = "StepWaiting";
    public const string StepCompleted     = "StepCompleted";
    public const string StepFailed        = "StepFailed";
    public const string StepRetryScheduled = "StepRetryScheduled";
    public const string WorkflowCompleted  = "WorkflowCompleted";
    public const string WorkflowFailed     = "WorkflowFailed";
    /// <summary>Manual retry from the last failed step (attempt counter reset to 1).</summary>
    public const string WorkflowRetried   = "WorkflowRetried";
    /// <summary>Full replay from step 1.</summary>
    public const string WorkflowReplayed  = "WorkflowReplayed";
    /// <summary>Manual cancellation of the workflow instance.</summary>
    public const string WorkflowCancelled = "WorkflowCancelled";
    /// <summary>Instance moved to archive — hidden from default list view.</summary>
    public const string WorkflowArchived  = "WorkflowArchived";
    /// <summary>Step handler exceeded its configured timeout and was cancelled.</summary>
    public const string StepTimedOut      = "StepTimedOut";
    /// <summary>Step was found in Running state with an expired lock — worker likely crashed.</summary>
    public const string StepOrphaned      = "StepOrphaned";
}
