using StepTrail.Shared.Workflows;

namespace StepTrail.Worker.Handlers;

public sealed class ProvisionAccountHandler : IStepExecutor
{
    private readonly ILogger<ProvisionAccountHandler> _logger;

    public ProvisionAccountHandler(ILogger<ProvisionAccountHandler> logger)
        => _logger = logger;

    public Task<StepExecutionResult> ExecuteAsync(StepExecutionRequest request, CancellationToken ct)
    {
        _logger.LogInformation(
            "Provisioning account for workflow instance {InstanceId}",
            request.WorkflowInstanceId);

        return Task.FromResult(StepExecutionResult.Success());
    }
}
