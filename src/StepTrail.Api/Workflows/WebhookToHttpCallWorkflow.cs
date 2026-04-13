using StepTrail.Shared.Workflows;

namespace StepTrail.Api.Workflows;

/// <summary>
/// Packaged template workflow: receives an external trigger (webhook) and
/// forwards the payload to a configurable HTTP endpoint with automatic retry.
///
/// The target URL is stored as the "webhook-to-http-call-url" secret so it
/// can be changed without redeploying. Set it up via the Templates page in the
/// Ops console.
/// </summary>
public sealed class WebhookToHttpCallWorkflow : WorkflowDescriptor
{
    public override string Key        => "webhook-to-http-call";
    public override int    Version    => 1;
    public override string Name       => "Webhook → HTTP Call";
    public override string? Description =>
        "Receives a webhook and forwards the payload to a configurable HTTP endpoint. Retries automatically on failure.";

    public override IReadOnlyList<WorkflowStepDescriptor> Steps =>
    [
        new WorkflowStepDescriptor(
            stepKey:            "http-call",
            stepType:           "HttpActivityHandler",
            order:              1,
            maxAttempts:        3,
            retryDelaySeconds:  30,
            timeoutSeconds:     30,
            config: new
            {
                Url    = "{{secrets.webhook-to-http-call-url}}",
                Method = "POST"
            })
    ];
}
