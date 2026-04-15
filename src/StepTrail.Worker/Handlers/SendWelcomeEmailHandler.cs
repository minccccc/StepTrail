using StepTrail.Shared.Workflows;

namespace StepTrail.Worker.Handlers;

public sealed class SendWelcomeEmailHandler : IStepExecutor
{
    private readonly ILogger<SendWelcomeEmailHandler> _logger;

    public SendWelcomeEmailHandler(ILogger<SendWelcomeEmailHandler> logger)
        => _logger = logger;

    public Task<StepExecutionResult> ExecuteAsync(StepExecutionRequest request, CancellationToken ct)
    {
        _logger.LogInformation(
            "Sending welcome email for workflow instance {InstanceId}",
            request.WorkflowInstanceId);

        return Task.FromResult(StepExecutionResult.Success());
    }
}
