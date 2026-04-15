using StepTrail.Api.Models;
using Microsoft.Extensions.DependencyInjection;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Runtime;
using StepTrail.Shared.Workflows;
using StepTrail.Shared;

namespace StepTrail.Api.Services;

public sealed class WorkflowInstanceService
{
    private readonly WorkflowStartService _workflowStartService;

    [ActivatorUtilitiesConstructor]
    public WorkflowInstanceService(WorkflowStartService workflowStartService)
    {
        _workflowStartService = workflowStartService;
    }

    public WorkflowInstanceService(
        StepTrailDbContext db,
        IWorkflowRegistry registry,
        IWorkflowDefinitionRepository workflowDefinitionRepository)
        : this(new WorkflowStartService(db, registry, workflowDefinitionRepository))
    {
    }

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
