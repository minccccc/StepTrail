namespace StepTrail.Worker.StepExecutors;

public static class StepExecutorKeys
{
    public const string HttpRequest = "HttpActivityHandler";
    public const string SendWebhook = "SendWebhookStepExecutor";
    public const string Transform = "TransformStepExecutor";
    public const string Conditional = "ConditionalStepExecutor";
    public const string Delay = "DelayStepExecutor";
}
