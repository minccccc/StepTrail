using StepTrail.Shared.Workflows;

namespace StepTrail.Api.Workflows;

/// <summary>
/// Sample workflow demonstrating code-first workflow definition.
/// Each step references a handler by name — handlers are implemented in PBI-06.
/// </summary>
public sealed class UserOnboardingWorkflow : WorkflowDescriptor
{
    public override string Key => "user-onboarding";
    public override int Version => 1;
    public override string Name => "User Onboarding";
    public override string? Description => "Onboards a new user: sends welcome email, provisions account, and notifies the team.";

    public override IReadOnlyList<WorkflowStepDescriptor> Steps =>
    [
        new WorkflowStepDescriptor("send-welcome-email",  "SendWelcomeEmailHandler",  1),
        new WorkflowStepDescriptor("provision-account",   "ProvisionAccountHandler",   2),
        new WorkflowStepDescriptor("notify-team",         "NotifyTeamHandler",         3),
    ];
}
