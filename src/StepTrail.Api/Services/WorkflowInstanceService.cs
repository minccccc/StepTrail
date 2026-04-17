using StepTrail.Api.Models;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Runtime;
using StepTrail.Shared.Telemetry;
using StepTrail.Shared.Workflows;
using StepTrail.Shared;

namespace StepTrail.Api.Services;

public sealed class WorkflowInstanceService
{
    private readonly WorkflowStartService _workflowStartService;
    private readonly TelemetryService? _telemetry;

    public WorkflowInstanceService(WorkflowStartService workflowStartService, TelemetryService telemetry)
    {
        _workflowStartService = workflowStartService;
        _telemetry = telemetry;
    }

    /// <summary>Test-only factory — creates without telemetry.</summary>
    public static WorkflowInstanceService CreateForTest(
        StepTrailDbContext db, IWorkflowRegistry registry, IWorkflowDefinitionRepository repo) =>
        new(new WorkflowStartService(db, registry, repo), null!);

    public async Task<(StartWorkflowResponse Response, bool Created)> StartAsync(
        StartWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var result = await _workflowStartService.StartAsync(
                new WorkflowStartRequest
                {
                    WorkflowKey = request.WorkflowKey,
                    Version = request.Version,
                    TenantId = request.TenantId,
                    ExternalKey = request.ExternalKey,
                    IdempotencyKey = request.IdempotencyKey,
                    Input = request.Input,
                    TriggerData = request.TriggerData
                },
                cancellationToken);

            if (result.Created && _telemetry is not null)
            {
                await _telemetry.RecordAsync(
                    TelemetryEvents.WorkflowStarted, TelemetryEvents.Categories.Execution, cancellationToken,
                    workflowKey: result.WorkflowKey, workflowInstanceId: result.Id, status: result.Status);
            }

            return (MapToResponse(result), result.Created);
        }
        catch (WorkflowStartNotFoundException ex)
        {
            throw new WorkflowNotFoundException(ex.Message);
        }
        catch (WorkflowStartTenantNotFoundException ex)
        {
            throw new TenantNotFoundException(ex.Message);
        }
        catch (WorkflowStartDefinitionNotActiveException ex)
        {
            throw new WorkflowDefinitionNotActiveException(ex.Message);
        }
    }

    private static StartWorkflowResponse MapToResponse(WorkflowStartResult result) =>
        new()
        {
            Id = result.Id,
            WorkflowKey = result.WorkflowKey,
            Version = result.Version,
            TenantId = result.TenantId,
            Status = result.Status,
            ExternalKey = result.ExternalKey,
            IdempotencyKey = result.IdempotencyKey,
            FirstStepExecutionId = result.FirstStepExecutionId,
            CreatedAt = result.CreatedAt,
            WasAlreadyStarted = result.WasAlreadyStarted
        };
}

public sealed class WorkflowNotFoundException : Exception
{
    public WorkflowNotFoundException(string message) : base(message)
    {
    }
}

public sealed class WorkflowDefinitionNotActiveException : Exception
{
    public WorkflowDefinitionNotActiveException(string message) : base(message)
    {
    }
}

public sealed class TenantNotFoundException : Exception
{
    public TenantNotFoundException(string message) : base(message)
    {
    }
}
