using StepTrail.Shared.Workflows;

namespace StepTrail.Api.Workflows;

/// <summary>
/// Built-in template: Webhook → Transform → Forward
///
/// The simplest and most canonical StepTrail workflow shape:
///   1. Receive a webhook payload
///   2. Normalize/transform the input into a clean structure
///   3. Forward the result to a downstream HTTP endpoint
///
/// This is the golden path template — the first template most users should try.
/// Demonstrates: durable webhook intake, data mapping, controlled outbound action,
/// visible failure, retry/replay usefulness.
/// </summary>
public sealed class WebhookTransformForwardWorkflow : WorkflowDescriptor
{
    public override string Key => "webhook-transform-forward";
    public override int Version => 1;
    public override string Name => "Webhook → Transform → Forward";
    public override string? Description =>
        "Receives a webhook, normalizes the payload, and forwards it to a downstream HTTP endpoint. " +
        "Retries automatically on failure. The simplest starting point for webhook-driven integrations.";

    public override IReadOnlyList<WorkflowStepDescriptor> Steps =>
    [
        new WorkflowStepDescriptor(
            stepKey: "transform-input",
            stepType: "Transform",
            order: 1,
            config: new
            {
                Mappings = new[]
                {
                    new { Target = "eventType", Source = "{{input.type}}" },
                    new { Target = "payload", Source = "{{input.data}}" },
                    new { Target = "receivedAt", Source = "{{input.timestamp}}" }
                }
            }),

        new WorkflowStepDescriptor(
            stepKey: "forward-payload",
            stepType: "HttpRequest",
            order: 2,
            maxAttempts: 3,
            retryDelaySeconds: 15,
            timeoutSeconds: 30,
            config: new
            {
                Url = "{{secrets.forward-destination-url}}",
                Method = "POST",
                Headers = new { Content_Type = "application/json" },
                Body = (string?)null
            })
    ];
}
