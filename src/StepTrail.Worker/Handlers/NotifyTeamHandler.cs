using StepTrail.Shared.Workflows;

namespace StepTrail.Worker.Handlers;

public sealed class NotifyTeamHandler : IStepHandler
{
    private readonly ILogger<NotifyTeamHandler> _logger;

    public NotifyTeamHandler(ILogger<NotifyTeamHandler> logger)
        => _logger = logger;

    public Task<StepResult> ExecuteAsync(StepContext context, CancellationToken ct)
    {
        _logger.LogInformation(
            "Notifying team for workflow instance {InstanceId}",
            context.WorkflowInstanceId);

        return Task.FromResult(StepResult.Success());
    }
}
