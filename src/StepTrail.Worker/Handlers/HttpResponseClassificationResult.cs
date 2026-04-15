using StepTrail.Shared.Workflows;

namespace StepTrail.Worker.Handlers;

public sealed class HttpResponseClassificationResult
{
    private HttpResponseClassificationResult(
        bool isSuccess,
        StepExecutionFailureClassification? failureClassification)
    {
        if (isSuccess && failureClassification is not null)
            throw new ArgumentException("Successful HTTP classification must not include a failure classification.", nameof(failureClassification));

        if (!isSuccess && failureClassification is null)
            throw new ArgumentException("Failed HTTP classification must include a failure classification.", nameof(failureClassification));

        IsSuccess = isSuccess;
        FailureClassification = failureClassification;
    }

    public bool IsSuccess { get; }
    public StepExecutionFailureClassification? FailureClassification { get; }

    public static HttpResponseClassificationResult Success() => new(true, null);

    public static HttpResponseClassificationResult Failure(StepExecutionFailureClassification failureClassification) =>
        new(false, failureClassification);
}
