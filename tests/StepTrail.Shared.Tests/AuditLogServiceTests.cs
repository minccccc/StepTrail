using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StepTrail.Shared.AuditLog;
using Xunit;

namespace StepTrail.Shared.Tests;

public class AuditLogServiceTests
{
    [Fact]
    public async Task RecordAsync_PersistsEventWithAllFields()
    {
        using var db = TestDbContextFactory.Create();
        var service = new AuditLogService(db, NullLogger<AuditLogService>.Instance);
        var defId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        await service.RecordAsync(
            AuditLogEvents.WorkflowActivated,
            AuditLogEvents.Categories.Authoring,
            CancellationToken.None,
            workflowKey: "test-workflow",
            workflowDefinitionId: defId,
            workflowInstanceId: instanceId,
            triggerType: "Manual",
            stepType: "HttpRequest",
            metadata: new { stepCount = 3 },
            actorId: "admin");

        var evt = Assert.Single(db.AuditLogEvents.ToList());
        Assert.Equal(AuditLogEvents.WorkflowActivated, evt.EventName);
        Assert.Equal(AuditLogEvents.Categories.Authoring, evt.Category);
        Assert.Equal("test-workflow", evt.WorkflowKey);
        Assert.Equal(defId, evt.WorkflowDefinitionId);
        Assert.Equal(instanceId, evt.WorkflowInstanceId);
        Assert.Equal("Manual", evt.TriggerType);
        Assert.Equal("HttpRequest", evt.StepType);
        Assert.Equal("admin", evt.ActorId);
        Assert.Contains("stepCount", evt.Metadata);
        Assert.True(evt.OccurredAtUtc > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task RecordAsync_PersistsEventWithMinimalFields()
    {
        using var db = TestDbContextFactory.Create();
        var service = new AuditLogService(db, NullLogger<AuditLogService>.Instance);

        await service.RecordAsync(
            AuditLogEvents.WorkflowCreatedBlank,
            AuditLogEvents.Categories.Authoring,
            CancellationToken.None,
            workflowKey: "my-workflow");

        var evt = Assert.Single(db.AuditLogEvents.ToList());
        Assert.Equal(AuditLogEvents.WorkflowCreatedBlank, evt.EventName);
        Assert.Equal("my-workflow", evt.WorkflowKey);
        Assert.Null(evt.WorkflowInstanceId);
        Assert.Null(evt.TriggerType);
        Assert.Null(evt.StepType);
        Assert.Null(evt.Metadata);
        Assert.Null(evt.ActorId);
    }

    [Fact]
    public async Task RecordAsync_SerializesMetadataAsJson()
    {
        using var db = TestDbContextFactory.Create();
        var service = new AuditLogService(db, NullLogger<AuditLogService>.Instance);

        await service.RecordAsync(
            AuditLogEvents.WorkflowCreatedFromTemplate,
            AuditLogEvents.Categories.Authoring,
            CancellationToken.None,
            metadata: new { descriptorKey = "webhook-to-http-call", descriptorVersion = 1 });

        var evt = Assert.Single(db.AuditLogEvents.ToList());
        Assert.Contains("webhook-to-http-call", evt.Metadata);
        Assert.Contains("descriptorVersion", evt.Metadata);
    }

    [Fact]
    public async Task RecordAsync_NeverThrows_OnPersistenceFailure()
    {
        // Use a disposed DbContext to simulate a persistence failure
        var db = TestDbContextFactory.Create();
        var service = new AuditLogService(db, NullLogger<AuditLogService>.Instance);
        db.Dispose();

        // Should not throw — telemetry is best-effort
        await service.RecordAsync(
            AuditLogEvents.WorkflowFailed,
            AuditLogEvents.Categories.Execution,
            CancellationToken.None,
            workflowKey: "failing-workflow");
    }

    [Fact]
    public void AuditLogEvents_HasExpectedEventNames()
    {
        Assert.Equal("template_selected", AuditLogEvents.TemplateSelected);
        Assert.Equal("workflow_created_blank", AuditLogEvents.WorkflowCreatedBlank);
        Assert.Equal("workflow_created_from_template", AuditLogEvents.WorkflowCreatedFromTemplate);
        Assert.Equal("workflow_cloned", AuditLogEvents.WorkflowCloned);
        Assert.Equal("workflow_activated", AuditLogEvents.WorkflowActivated);
        Assert.Equal("workflow_deactivated", AuditLogEvents.WorkflowDeactivated);
        Assert.Equal("trigger_type_changed", AuditLogEvents.TriggerTypeChanged);
        Assert.Equal("step_added", AuditLogEvents.StepAdded);
        Assert.Equal("workflow_started", AuditLogEvents.WorkflowStarted);
        Assert.Equal("workflow_completed", AuditLogEvents.WorkflowCompleted);
        Assert.Equal("workflow_failed", AuditLogEvents.WorkflowFailed);
        Assert.Equal("manual_retry_triggered", AuditLogEvents.ManualRetryTriggered);
        Assert.Equal("replay_triggered", AuditLogEvents.ReplayTriggered);
        Assert.Equal("activation_failed", AuditLogEvents.ActivationFailed);
    }

    [Fact]
    public void TelemetryCategories_HasExpectedValues()
    {
        Assert.Equal("Authoring", AuditLogEvents.Categories.Authoring);
        Assert.Equal("Execution", AuditLogEvents.Categories.Execution);
        Assert.Equal("Error", AuditLogEvents.Categories.Error);
    }
}
