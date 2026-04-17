using Microsoft.EntityFrameworkCore;
using StepTrail.Api.Models;
using StepTrail.Shared;
using StepTrail.Shared.Entities;

namespace StepTrail.Api.Endpoints;

public static class OpsEndpoints
{
    public static RouteGroupBuilder MapOpsEndpoints(this RouteGroupBuilder ops)
    {
        // ── Secrets management ────────────────────────────────────────────────────────
        // Values are never returned by the API; only names and descriptions are exposed.

        ops.MapGet("/secrets", async (StepTrailDbContext db, CancellationToken ct) =>
        {
            var secrets = await db.WorkflowSecrets
                .OrderBy(s => s.Name)
                .Select(s => new { s.Name, s.Description, s.UpdatedAt })
                .ToListAsync(ct);
            return Results.Ok(secrets);
        });

        ops.MapPut("/secrets/{name}", async (
            string name,
            UpsertSecretRequest req,
            StepTrailDbContext db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Value))
                return Results.BadRequest(new { error = "Value is required." });

            var existing = await db.WorkflowSecrets
                .FirstOrDefaultAsync(s => s.Name == name, ct);

            var now = DateTimeOffset.UtcNow;

            if (existing is null)
            {
                db.WorkflowSecrets.Add(new WorkflowSecret
                {
                    Id          = Guid.NewGuid(),
                    Name        = name,
                    Value       = req.Value,
                    Description = req.Description,
                    CreatedAt   = now,
                    UpdatedAt   = now
                });
                await db.SaveChangesAsync(ct);
                return Results.Created($"/secrets/{name}", new { name });
            }

            existing.Value       = req.Value;
            existing.Description = req.Description ?? existing.Description;
            existing.UpdatedAt   = now;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { name });
        });

        ops.MapDelete("/secrets/{name}", async (
            string name,
            StepTrailDbContext db,
            CancellationToken ct) =>
        {
            var secret = await db.WorkflowSecrets
                .FirstOrDefaultAsync(s => s.Name == name, ct);

            if (secret is null) return Results.NotFound(new { error = $"Secret '{name}' not found." });

            db.WorkflowSecrets.Remove(secret);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // ── Telemetry ────────────────────────────────────────────────────────────────

        ops.MapGet("/telemetry", async (
            string? category,
            int? days,
            StepTrailDbContext db,
            CancellationToken ct) =>
        {
            var since = DateTimeOffset.UtcNow.AddDays(-(days ?? 30));

            var query = db.PilotTelemetryEvents
                .Where(e => e.OccurredAtUtc >= since);

            if (!string.IsNullOrWhiteSpace(category))
                query = query.Where(e => e.Category == category);

            var rawEvents = await query
                .OrderByDescending(e => e.OccurredAtUtc)
                .Take(500)
                .ToListAsync(ct);

            // Batch-load current statuses from the actual tables
            var instanceIds = rawEvents
                .Where(e => e.WorkflowInstanceId.HasValue)
                .Select(e => e.WorkflowInstanceId!.Value)
                .Distinct().ToList();

            var instanceStatuses = instanceIds.Count > 0
                ? await db.WorkflowInstances
                    .Where(i => instanceIds.Contains(i.Id))
                    .Select(i => new { i.Id, i.Status })
                    .ToDictionaryAsync(i => i.Id, i => i.Status.ToString(), ct)
                : new Dictionary<Guid, string>();

            var definitionIds = rawEvents
                .Where(e => e.WorkflowDefinitionId.HasValue && !e.WorkflowInstanceId.HasValue)
                .Select(e => e.WorkflowDefinitionId!.Value)
                .Distinct().ToList();

            var definitionStatuses = definitionIds.Count > 0
                ? await db.ExecutableWorkflowDefinitions
                    .Where(d => definitionIds.Contains(d.Id))
                    .Select(d => new { d.Id, d.Status })
                    .ToDictionaryAsync(d => d.Id, d => d.Status.ToString(), ct)
                : new Dictionary<Guid, string>();

            var events = rawEvents.Select(e =>
            {
                // Resolve current status: instance status takes priority, then definition status
                string? currentStatus = null;
                if (e.WorkflowInstanceId.HasValue)
                    instanceStatuses.TryGetValue(e.WorkflowInstanceId.Value, out currentStatus);
                else if (e.WorkflowDefinitionId.HasValue)
                    definitionStatuses.TryGetValue(e.WorkflowDefinitionId.Value, out currentStatus);

                return new
                {
                    e.EventName,
                    e.Category,
                    e.OccurredAtUtc,
                    e.WorkflowKey,
                    e.WorkflowDefinitionId,
                    e.WorkflowInstanceId,
                    Status = currentStatus,
                    e.TriggerType,
                    e.StepType,
                    e.ActorId,
                    e.Metadata
                };
            });

            var summary = await db.PilotTelemetryEvents
                .Where(e => e.OccurredAtUtc >= since)
                .GroupBy(e => new { e.Category, e.EventName })
                .Select(g => new { g.Key.Category, g.Key.EventName, Count = g.Count() })
                .OrderBy(g => g.Category)
                .ThenByDescending(g => g.Count)
                .ToListAsync(ct);

            return Results.Ok(new { since, summary, recentEvents = events });
        });

        return ops;
    }
}
