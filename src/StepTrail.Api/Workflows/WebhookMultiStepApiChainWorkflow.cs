using StepTrail.Shared.Workflows;

namespace StepTrail.Api.Workflows;

/// <summary>
/// Built-in template: Webhook → Multi-step API Chain
///
/// Demonstrates a real multi-step operational workflow:
///   1. Receive a webhook payload
///   2. Transform input for the first API call
///   3. Call API A
///   4. Transform API A's result for the second API call
///   5. Call API B
///
/// Proves StepTrail is more than a reliable forwarder — shows multi-step execution,
/// data flow between steps, partial failure handling, and the value of replay
/// (failure in step 5 does not re-run steps 1–4).
/// </summary>
public sealed class WebhookMultiStepApiChainWorkflow : WorkflowDescriptor
{
    public override string Key => "webhook-api-chain";
    public override int Version => 1;
    public override string Name => "Webhook → API Chain";
    public override string? Description =>
        "Receives a webhook, transforms the payload, calls API A, transforms the result, " +
        "then calls API B. Demonstrates multi-step execution with data flow between steps. " +
        "Failure in later steps can be retried without re-running earlier completed steps.";

    public override IReadOnlyList<WorkflowStepDescriptor> Steps =>
    [
        new WorkflowStepDescriptor(
            stepKey: "transform-for-api-a",
            stepType: "Transform",
            order: 1,
            config: new
            {
                Mappings = new[]
                {
                    new { Target = "requestId", Source = "{{input.id}}" },
                    new { Target = "action", Source = "{{input.action}}" },
                    new { Target = "data", Source = "{{input.payload}}" }
                }
            }),

        new WorkflowStepDescriptor(
            stepKey: "call-api-a",
            stepType: "HttpRequest",
            order: 2,
            maxAttempts: 3,
            retryDelaySeconds: 10,
            timeoutSeconds: 30,
            config: new
            {
                Url = "{{secrets.api-a-url}}",
                Method = "POST",
                Headers = new { Authorization = "Bearer {{secrets.api-a-token}}" },
                Body = (string?)null
            }),

        new WorkflowStepDescriptor(
            stepKey: "transform-for-api-b",
            stepType: "Transform",
            order: 3,
            config: new
            {
                Mappings = new[]
                {
                    new { Target = "sourceId", Source = "{{steps.call-api-a.output.id}}" },
                    new { Target = "status", Source = "{{steps.call-api-a.output.status}}" },
                    new { Target = "originalRequestId", Source = "{{steps.transform-for-api-a.output.requestId}}" }
                }
            }),

        new WorkflowStepDescriptor(
            stepKey: "call-api-b",
            stepType: "HttpRequest",
            order: 4,
            maxAttempts: 3,
            retryDelaySeconds: 15,
            timeoutSeconds: 30,
            config: new
            {
                Url = "{{secrets.api-b-url}}",
                Method = "POST",
                Headers = new { Authorization = "Bearer {{secrets.api-b-token}}" },
                Body = (string?)null
            })
    ];
}
