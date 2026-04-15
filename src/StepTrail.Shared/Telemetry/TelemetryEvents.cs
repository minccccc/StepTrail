namespace StepTrail.Shared.Telemetry;

/// <summary>
/// Stable event name constants for pilot telemetry instrumentation.
/// Grouped by category to make it clear what each event represents.
/// </summary>
public static class TelemetryEvents
{
    public static class Categories
    {
        public const string Authoring = "Authoring";
        public const string Execution = "Execution";
        public const string Error = "Error";
    }

    // ── Authoring ────────────────────────────────────────────────────────────
    public const string TemplateSelected = "template_selected";
    public const string WorkflowCreatedBlank = "workflow_created_blank";
    public const string WorkflowCreatedFromTemplate = "workflow_created_from_template";
    public const string WorkflowCloned = "workflow_cloned";
    public const string WorkflowActivated = "workflow_activated";
    public const string WorkflowDeactivated = "workflow_deactivated";
    public const string TriggerTypeChanged = "trigger_type_changed";
    public const string TriggerConfigSaved = "trigger_config_saved";
    public const string StepAdded = "step_added";
    public const string StepConfigSaved = "step_config_saved";
    public const string StepRemoved = "step_removed";

    // ── Execution ────────────────────────────────────────────────────────────
    public const string WorkflowStarted = "workflow_started";
    public const string WorkflowCompleted = "workflow_completed";
    public const string WorkflowFailed = "workflow_failed";
    public const string ManualRetryTriggered = "manual_retry_triggered";
    public const string ReplayTriggered = "replay_triggered";

    // ── Error / Friction ─────────────────────────────────────────────────────
    public const string ActivationFailed = "activation_failed";
    public const string AlertDeliveryFailed = "alert_delivery_failed";
}
