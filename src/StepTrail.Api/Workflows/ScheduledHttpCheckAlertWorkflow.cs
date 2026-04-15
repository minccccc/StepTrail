using StepTrail.Shared.Workflows;

namespace StepTrail.Api.Workflows;

/// <summary>
/// Built-in template: Scheduled HTTP Check → Alert
///
/// Validates StepTrail for non-webhook use cases:
///   1. Run on a schedule (interval or cron)
///   2. Call an HTTP endpoint (health check, dependency check)
///   3. Evaluate the result with a conditional
///   4. Send an alert webhook if the check fails
///
/// Proves: Schedule trigger, operational polling pattern, Conditional step,
/// outbound notification, and a simple monitoring/health-check workflow.
/// </summary>
public sealed class ScheduledHttpCheckAlertWorkflow : WorkflowDescriptor
{
    public override string Key => "scheduled-http-check";
    public override int Version => 1;
    public override string Name => "Scheduled HTTP Check → Alert";
    public override string? Description =>
        "Runs on a schedule, calls an HTTP endpoint to check health or status, " +
        "evaluates the response, and sends an alert webhook if the check fails. " +
        "A simple operational monitoring pattern — no webhook trigger needed.";

    public override int? RecurrenceIntervalSeconds => 300;

    public override IReadOnlyList<WorkflowStepDescriptor> Steps =>
    [
        new WorkflowStepDescriptor(
            stepKey: "check-endpoint",
            stepType: "HttpRequest",
            order: 1,
            maxAttempts: 2,
            retryDelaySeconds: 10,
            timeoutSeconds: 15,
            config: new
            {
                Url = "{{secrets.check-endpoint-url}}",
                Method = "GET"
            }),

        new WorkflowStepDescriptor(
            stepKey: "evaluate-result",
            stepType: "Conditional",
            order: 2,
            config: new
            {
                SourcePath = "{{steps.check-endpoint.output.statusCode}}",
                Operator = "Equals",
                ExpectedValue = "200",
                FalseOutcome = "CompleteWorkflow"
            }),

        new WorkflowStepDescriptor(
            stepKey: "send-alert",
            stepType: "SendWebhook",
            order: 3,
            maxAttempts: 3,
            retryDelaySeconds: 30,
            timeoutSeconds: 15,
            config: new
            {
                Url = "{{secrets.alert-webhook-url}}",
                Method = "POST",
                Body = (string?)null
            })
    ];
}
