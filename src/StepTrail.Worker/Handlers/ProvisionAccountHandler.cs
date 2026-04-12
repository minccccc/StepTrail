using StepTrail.Shared.Workflows;

namespace StepTrail.Worker.Handlers;

public sealed class ProvisionAccountHandler : IStepHandler
{
    private readonly ILogger<ProvisionAccountHandler> _logger;

    public ProvisionAccountHandler(ILogger<ProvisionAccountHandler> logger)
        => _logger = logger;

    public Task<StepResult> ExecuteAsync(StepContext context, CancellationToken ct)
    {
        _logger.LogInformation(
            "Provisioning account for workflow instance {InstanceId}",
            context.WorkflowInstanceId);

        return Task.FromResult(StepResult.Success());
    }
}
