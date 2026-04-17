using System.Security.Claims;
using StepTrail.Api.Models;
using StepTrail.Api.Services;
using StepTrail.Shared.AuditLog;

namespace StepTrail.Api.Endpoints;

public static class InstanceEndpoints
{
    public static RouteGroupBuilder MapInstanceEndpoints(this RouteGroupBuilder ops)
    {
        ops.MapPost("/manual-triggers/start", async (
            StartManualWorkflowRequest request,
            ClaimsPrincipal user,
            ManualWorkflowTriggerService service,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.WorkflowKey))
                return Results.BadRequest(new { error = "WorkflowKey is required." });

            if (request.TenantId == Guid.Empty)
                return Results.BadRequest(new { error = "TenantId is required." });

            if (string.IsNullOrWhiteSpace(request.ActorId))
            {
                request.ActorId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? user.Identity?.Name;
            }

            try
            {
                var (response, created) = await service.StartAsync(request, ct);
                return created
                    ? Results.Created($"/workflow-instances/{response.Id}", response)
                    : Results.Ok(response);
            }
            catch (WorkflowNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (TenantNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (WorkflowDefinitionNotActiveException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (WorkflowTriggerMismatchException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        ops.MapPost("/workflow-instances", async (
            StartWorkflowRequest request,
            WorkflowInstanceService service,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.WorkflowKey))
                return Results.BadRequest(new { error = "WorkflowKey is required." });

            if (request.TenantId == Guid.Empty)
                return Results.BadRequest(new { error = "TenantId is required." });

            try
            {
                var (response, created) = await service.StartAsync(request, ct);
                return created
                    ? Results.Created($"/workflow-instances/{response.Id}", response)
                    : Results.Ok(response);
            }
            catch (WorkflowNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (TenantNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (WorkflowDefinitionNotActiveException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        ops.MapGet("/workflow-instances", async (
            Guid? tenantId,
            string? workflowKey,
            string? status,
            bool? includeArchived,
            int? page,
            int? pageSize,
            DateTimeOffset? createdFrom,
            DateTimeOffset? createdTo,
            string? triggerType,
            WorkflowQueryService service,
            CancellationToken ct) =>
        {
            var effectivePage = Math.Max(page ?? 1, 1);
            var effectivePageSize = Math.Clamp(pageSize ?? 20, 1, 100);
            try
            {
                var result = await service.ListAsync(
                    tenantId, workflowKey, status,
                    includeArchived ?? false,
                    effectivePage, effectivePageSize, ct,
                    createdFrom, createdTo, triggerType);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        ops.MapGet("/workflow-instances/{id:guid}", async (
            Guid id,
            WorkflowQueryService service,
            CancellationToken ct) =>
        {
            try
            {
                var result = await service.GetDetailAsync(id, ct);
                return Results.Ok(result);
            }
            catch (WorkflowInstanceNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        ops.MapGet("/workflow-instances/{id:guid}/trail", async (
            Guid id,
            WorkflowQueryService service,
            CancellationToken ct) =>
        {
            try
            {
                var result = await service.GetTrailAsync(id, ct);
                return Results.Ok(result);
            }
            catch (WorkflowInstanceNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        ops.MapGet("/workflow-instances/{id:guid}/timeline", async (
            Guid id,
            WorkflowQueryService service,
            CancellationToken ct) =>
        {
            try
            {
                var result = await service.GetTimelineAsync(id, ct);
                return Results.Ok(result);
            }
            catch (WorkflowInstanceNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        ops.MapPost("/workflow-instances/{id:guid}/retry", async (
            Guid id,
            WorkflowRetryService service,
            AuditLogService telemetry,
            CancellationToken ct) =>
        {
            try
            {
                var response = await service.RetryAsync(id, ct);
                await telemetry.RecordAsync(AuditLogEvents.ManualRetryTriggered, AuditLogEvents.Categories.Execution, ct,
                    workflowKey: response.WorkflowKey, workflowInstanceId: id, status: response.InstanceStatus);
                return Results.Ok(response);
            }
            catch (WorkflowInstanceNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidWorkflowStateException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        ops.MapPost("/workflow-instances/{id:guid}/replay", async (
            Guid id,
            WorkflowRetryService service,
            AuditLogService telemetry,
            CancellationToken ct) =>
        {
            try
            {
                var response = await service.ReplayAsync(id, ct);
                await telemetry.RecordAsync(AuditLogEvents.ReplayTriggered, AuditLogEvents.Categories.Execution, ct,
                    workflowKey: response.WorkflowKey, workflowInstanceId: id, status: response.InstanceStatus);
                return Results.Ok(response);
            }
            catch (WorkflowInstanceNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidWorkflowStateException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        ops.MapPost("/workflow-instances/{id:guid}/archive", async (
            Guid id,
            WorkflowRetryService service,
            CancellationToken ct) =>
        {
            try
            {
                var response = await service.ArchiveAsync(id, ct);
                return Results.Ok(response);
            }
            catch (WorkflowInstanceNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidWorkflowStateException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        ops.MapPost("/workflow-instances/{id:guid}/cancel", async (
            Guid id,
            WorkflowRetryService service,
            CancellationToken ct) =>
        {
            try
            {
                var response = await service.CancelAsync(id, ct);
                return Results.Ok(response);
            }
            catch (WorkflowInstanceNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidWorkflowStateException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        return ops;
    }
}
