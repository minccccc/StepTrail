using StepTrail.Shared.Workflows;

namespace StepTrail.Worker.Handlers;

public sealed class NotifyTeamHandler : IStepExecutor
{
    private readonly ILogger<NotifyTeamHandler> _logger;

    public NotifyTeamHandler(ILogger<NotifyTeamHandler> logger)
        => _logger = logger;

    public Task<StepExecutionResult> ExecuteAsync(StepExecutionRequest request, CancellationToken ct)
    {
        _logger.LogInformation(
            "Notifying team for workflow instance {InstanceId}",
            request.WorkflowInstanceId);

        return Task.FromResult(StepExecutionResult.Success());
    }
}
