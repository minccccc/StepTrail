namespace StepTrail.Shared.Runtime;

public sealed class WorkflowStartRequest
{
    public string WorkflowKey { get; set; } = string.Empty;
    public int? Version { get; set; }
    public Guid TenantId { get; set; }
    public string? ExternalKey { get; set; }
    public string? IdempotencyKey { get; set; }
    public object? Input { get; set; }
    public string? TriggerData { get; set; }
}
