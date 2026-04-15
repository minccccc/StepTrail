using System.Text.Json;
using Microsoft.Extensions.Logging;
using StepTrail.Shared.Entities;

namespace StepTrail.Shared.Telemetry;

/// <summary>
/// Records structured pilot telemetry events to the database.
/// Best-effort — failures are logged but never block the caller.
/// Usable from both the API (authoring events) and Worker (execution events).
/// </summary>
public sealed class TelemetryService
{
    private readonly StepTrailDbContext _db;
    private readonly ILogger<TelemetryService> _logger;

    public TelemetryService(StepTrailDbContext db, ILogger<TelemetryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Records a telemetry event. Never throws — persistence failures are logged.
    /// </summary>
    public async Task RecordAsync(
        string eventName,
        string category,
        CancellationToken ct,
        string? workflowKey = null,
        Guid? workflowDefinitionId = null,
        Guid? workflowInstanceId = null,
        string? triggerType = null,
        string? stepType = null,
        object? metadata = null,
        string? actorId = null)
    {
        try
        {
            _db.PilotTelemetryEvents.Add(new PilotTelemetryEvent
            {
                Id = Guid.NewGuid(),
                EventName = eventName,
                Category = category,
                OccurredAtUtc = DateTimeOffset.UtcNow,
                WorkflowKey = workflowKey,
                WorkflowDefinitionId = workflowDefinitionId,
                WorkflowInstanceId = workflowInstanceId,
                TriggerType = triggerType,
                StepType = stepType,
                Metadata = metadata is not null
                    ? JsonSerializer.Serialize(metadata)
                    : null,
                ActorId = actorId
            });

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record telemetry event '{EventName}'", eventName);
        }
    }
}
