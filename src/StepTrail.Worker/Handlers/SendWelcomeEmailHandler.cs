using StepTrail.Shared.Workflows;

namespace StepTrail.Worker.Handlers;

public sealed class SendWelcomeEmailHandler : IStepHandler
{
    private readonly ILogger<SendWelcomeEmailHandler> _logger;

    public SendWelcomeEmailHandler(ILogger<SendWelcomeEmailHandler> logger)
        => _logger = logger;

    public Task<StepResult> ExecuteAsync(StepContext context, CancellationToken ct)
    {
        _logger.LogInformation(
            "Sending welcome email for workflow instance {InstanceId}",
            context.WorkflowInstanceId);

        return Task.FromResult(StepResult.Success());
    }
}
