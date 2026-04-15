namespace StepTrail.Shared.Workflows;

public enum StepExecutionFailureClassification
{
    TransientFailure = 1,
    PermanentFailure = 2,
    InvalidConfiguration = 3,
    InputResolutionFailure = 4
}
