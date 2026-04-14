using Xunit;
using StepTrail.Shared.Definitions;

namespace StepTrail.Shared.Tests.Definitions;

public class WorkflowDefinitionTests
{
    [Fact]
    public void Constructor_CreatesWorkflowDefinition_WithSingleTriggerAndOrderedSteps()
    {
        var createdAtUtc = new DateTimeOffset(2026, 4, 14, 10, 0, 0, TimeSpan.Zero);
        var updatedAtUtc = createdAtUtc.AddMinutes(5);
        var triggerDefinition = TriggerDefinition.CreateManual(
            Guid.NewGuid(),
            new ManualTriggerConfiguration("ops-console"));
        var secondStep = StepDefinition.CreateSendWebhook(
            Guid.NewGuid(),
            "send-email",
            2,
            new SendWebhookStepConfiguration("https://hooks.example.com/workflows"));
        var firstStep = StepDefinition.CreateHttpRequest(
            Guid.NewGuid(),
            "prepare-input",
            1,
            new HttpRequestStepConfiguration("https://api.example.com/input"));

        var definition = new WorkflowDefinition(
            Guid.NewGuid(),
            "user-onboarding",
            "User Onboarding",
            3,
            WorkflowDefinitionStatus.Draft,
            triggerDefinition,
            [secondStep, firstStep],
            createdAtUtc,
            updatedAtUtc,
            "  Internal executable workflow.  ");

        Assert.Equal("user-onboarding", definition.Key);
        Assert.Equal("User Onboarding", definition.Name);
        Assert.Equal(3, definition.Version);
        Assert.Equal(WorkflowDefinitionStatus.Draft, definition.Status);
        Assert.Same(triggerDefinition, definition.TriggerDefinition);
        Assert.Equal("Internal executable workflow.", definition.Description);
        Assert.Equal(createdAtUtc, definition.CreatedAtUtc);
        Assert.Equal(updatedAtUtc, definition.UpdatedAtUtc);
        Assert.Collection(
            definition.StepDefinitions,
            step => Assert.Equal("prepare-input", step.Key),
            step => Assert.Equal("send-email", step.Key));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_Throws_WhenKeyIsMissing(string key)
    {
        var ex = Assert.Throws<ArgumentException>(() => new WorkflowDefinition(
            Guid.NewGuid(),
            key,
            "Workflow Name",
            1,
            WorkflowDefinitionStatus.Active,
            CreateTrigger(),
            [CreateStep()],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));

        Assert.Equal("key", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_Throws_WhenNameIsMissing(string name)
    {
        var ex = Assert.Throws<ArgumentException>(() => new WorkflowDefinition(
            Guid.NewGuid(),
            "workflow-key",
            name,
            1,
            WorkflowDefinitionStatus.Active,
            CreateTrigger(),
            [CreateStep()],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));

        Assert.Equal("name", ex.ParamName);
    }

    [Fact]
    public void Constructor_Throws_WhenVersionIsLessThanOne()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new WorkflowDefinition(
            Guid.NewGuid(),
            "workflow-key",
            "Workflow Name",
            0,
            WorkflowDefinitionStatus.Active,
            CreateTrigger(),
            [CreateStep()],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));

        Assert.Equal("version", ex.ParamName);
    }

    [Fact]
    public void Constructor_Throws_WhenTriggerDefinitionIsMissing()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new WorkflowDefinition(
            Guid.NewGuid(),
            "workflow-key",
            "Workflow Name",
            1,
            WorkflowDefinitionStatus.Active,
            null!,
            [CreateStep()],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));

        Assert.Equal("triggerDefinition", ex.ParamName);
    }

    [Fact]
    public void Constructor_Throws_WhenStepDefinitionsAreEmpty()
    {
        var ex = Assert.Throws<ArgumentException>(() => new WorkflowDefinition(
            Guid.NewGuid(),
            "workflow-key",
            "Workflow Name",
            1,
            WorkflowDefinitionStatus.Active,
            CreateTrigger(),
            [],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));

        Assert.Equal("stepDefinitions", ex.ParamName);
    }

    [Fact]
    public void Constructor_Throws_WhenUpdatedTimestampPrecedesCreatedTimestamp()
    {
        var createdAtUtc = new DateTimeOffset(2026, 4, 14, 11, 0, 0, TimeSpan.Zero);
        var updatedAtUtc = createdAtUtc.AddMinutes(-1);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new WorkflowDefinition(
            Guid.NewGuid(),
            "workflow-key",
            "Workflow Name",
            1,
            WorkflowDefinitionStatus.Active,
            CreateTrigger(),
            [CreateStep()],
            createdAtUtc,
            updatedAtUtc));

        Assert.Equal("updatedAtUtc", ex.ParamName);
    }

    [Fact]
    public void Constructor_Throws_WhenStepKeysAreDuplicated()
    {
        var ex = Assert.Throws<ArgumentException>(() => new WorkflowDefinition(
            Guid.NewGuid(),
            "workflow-key",
            "Workflow Name",
            1,
            WorkflowDefinitionStatus.Active,
            CreateTrigger(),
            [
                StepDefinition.CreateDelay(Guid.NewGuid(), "duplicate-step", 1, new DelayStepConfiguration(5)),
                StepDefinition.CreateConditional(Guid.NewGuid(), "duplicate-step", 2, new ConditionalStepConfiguration("payload.ready == true"))
            ],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));

        Assert.Equal("stepDefinitions", ex.ParamName);
    }

    [Fact]
    public void Constructor_Throws_WhenStepOrdersAreDuplicated()
    {
        var ex = Assert.Throws<ArgumentException>(() => new WorkflowDefinition(
            Guid.NewGuid(),
            "workflow-key",
            "Workflow Name",
            1,
            WorkflowDefinitionStatus.Active,
            CreateTrigger(),
            [
                StepDefinition.CreateDelay(Guid.NewGuid(), "wait", 1, new DelayStepConfiguration(5)),
                StepDefinition.CreateConditional(Guid.NewGuid(), "check", 1, new ConditionalStepConfiguration("payload.ready == true"))
            ],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));

        Assert.Equal("stepDefinitions", ex.ParamName);
    }

    private static TriggerDefinition CreateTrigger() =>
        TriggerDefinition.CreateManual(Guid.NewGuid(), new ManualTriggerConfiguration("ops-console"));

    private static StepDefinition CreateStep() =>
        StepDefinition.CreateHttpRequest(
            Guid.NewGuid(),
            "step-1",
            1,
            new HttpRequestStepConfiguration("https://api.example.com/step-1"));
}
