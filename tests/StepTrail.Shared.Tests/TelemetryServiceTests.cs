using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StepTrail.Shared.Telemetry;
using Xunit;

namespace StepTrail.Shared.Tests;

public class TelemetryServiceTests
{
    [Fact]
    public async Task RecordAsync_PersistsEventWithAllFields()
    {
        using var db = TestDbContextFactory.Create();
        var service = new TelemetryService(db, NullLogger<TelemetryService>.Instance);
        var defId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        await service.RecordAsync(
            TelemetryEvents.WorkflowActivated,
            TelemetryEvents.Categories.Authoring,
            CancellationToken.None,
            workflowKey: "test-workflow",
            workflowDefinitionId: defId,
            workflowInstanceId: instanceId,
            triggerType: "Manual",
            stepType: "HttpRequest",
            metadata: new { stepCount = 3 },
            actorId: "admin");

        var evt = Assert.Single(db.PilotTelemetryEvents.ToList());
        Assert.Equal(TelemetryEvents.WorkflowActivated, evt.EventName);
        Assert.Equal(TelemetryEvents.Categories.Authoring, evt.Category);
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
        var service = new TelemetryService(db, NullLogger<TelemetryService>.Instance);

        await service.RecordAsync(
            TelemetryEvents.WorkflowCreatedBlank,
            TelemetryEvents.Categories.Authoring,
            CancellationToken.None,
            workflowKey: "my-workflow");

        var evt = Assert.Single(db.PilotTelemetryEvents.ToList());
        Assert.Equal(TelemetryEvents.WorkflowCreatedBlank, evt.EventName);
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
        var service = new TelemetryService(db, NullLogger<TelemetryService>.Instance);

        await service.RecordAsync(
            TelemetryEvents.WorkflowCreatedFromTemplate,
            TelemetryEvents.Categories.Authoring,
            CancellationToken.None,
            metadata: new { descriptorKey = "webhook-to-http-call", descriptorVersion = 1 });

        var evt = Assert.Single(db.PilotTelemetryEvents.ToList());
        Assert.Contains("webhook-to-http-call", evt.Metadata);
        Assert.Contains("descriptorVersion", evt.Metadata);
    }

    [Fact]
    public async Task RecordAsync_NeverThrows_OnPersistenceFailure()
    {
        // Use a disposed DbContext to simulate a persistence failure
        var db = TestDbContextFactory.Create();
        var service = new TelemetryService(db, NullLogger<TelemetryService>.Instance);
        db.Dispose();

        // Should not throw — telemetry is best-effort
        await service.RecordAsync(
            TelemetryEvents.WorkflowFailed,
            TelemetryEvents.Categories.Execution,
            CancellationToken.None,
            workflowKey: "failing-workflow");
    }

    [Fact]
    public void TelemetryEvents_HasExpectedEventNames()
    {
        Assert.Equal("template_selected", TelemetryEvents.TemplateSelected);
        Assert.Equal("workflow_created_blank", TelemetryEvents.WorkflowCreatedBlank);
        Assert.Equal("workflow_created_from_template", TelemetryEvents.WorkflowCreatedFromTemplate);
        Assert.Equal("workflow_cloned", TelemetryEvents.WorkflowCloned);
        Assert.Equal("workflow_activated", TelemetryEvents.WorkflowActivated);
        Assert.Equal("workflow_deactivated", TelemetryEvents.WorkflowDeactivated);
        Assert.Equal("trigger_type_changed", TelemetryEvents.TriggerTypeChanged);
        Assert.Equal("step_added", TelemetryEvents.StepAdded);
        Assert.Equal("workflow_started", TelemetryEvents.WorkflowStarted);
        Assert.Equal("workflow_completed", TelemetryEvents.WorkflowCompleted);
        Assert.Equal("workflow_failed", TelemetryEvents.WorkflowFailed);
        Assert.Equal("manual_retry_triggered", TelemetryEvents.ManualRetryTriggered);
        Assert.Equal("replay_triggered", TelemetryEvents.ReplayTriggered);
        Assert.Equal("activation_failed", TelemetryEvents.ActivationFailed);
    }

    [Fact]
    public void TelemetryCategories_HasExpectedValues()
    {
        Assert.Equal("Authoring", TelemetryEvents.Categories.Authoring);
        Assert.Equal("Execution", TelemetryEvents.Categories.Execution);
        Assert.Equal("Error", TelemetryEvents.Categories.Error);
    }
}
