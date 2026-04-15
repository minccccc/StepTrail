using System.Reflection;
using StepTrail.Shared.Definitions;
using Xunit;

namespace StepTrail.Shared.Tests.Definitions;

public class WorkflowDefinitionValidatorTests
{
    private readonly WorkflowDefinitionValidator _validator = new();

    [Fact]
    public void ValidateForActivation_ReturnsValidResult_ForExecutableDefinitionWithTriggerAndContiguousSteps()
    {
        var definition = CreateWorkflowDefinition(
            stepDefinitions:
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "fetch-customer",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/customers")),
                StepDefinition.CreateTransform(
                    Guid.NewGuid(),
                    "shape-payload",
                    2,
                    new TransformStepConfiguration(
                    [
                        new TransformValueMapping("$.customerId", "$.id")
                    ]))
            ]);

        var validationResult = _validator.ValidateForActivation(definition);

        Assert.True(validationResult.IsValid);
        Assert.Empty(validationResult.Errors);
    }

    [Fact]
    public void ValidateForActivation_ReturnsError_WhenTriggerIsMissing()
    {
        var definition = CreateWorkflowDefinition();
        SetProperty(definition, nameof(WorkflowDefinition.TriggerDefinition), null);

        var validationResult = _validator.ValidateForActivation(definition);

        var error = Assert.Single(validationResult.Errors);
        Assert.False(validationResult.IsValid);
        Assert.Equal("workflow.trigger.required", error.Code);
        Assert.Equal("triggerDefinition", error.Path);
        Assert.Equal(
            "Workflow definition must declare a trigger definition before it can be activated.",
            error.Message);
    }

    [Fact]
    public void ValidateForActivation_ReturnsError_WhenStepsAreMissing()
    {
        var definition = CreateWorkflowDefinition();
        ClearBackingStepDefinitions(definition);

        var validationResult = _validator.ValidateForActivation(definition);

        var error = Assert.Single(validationResult.Errors);
        Assert.False(validationResult.IsValid);
        Assert.Equal("workflow.steps.required", error.Code);
        Assert.Equal("stepDefinitions", error.Path);
    }

    [Fact]
    public void ValidateForActivation_ReturnsError_WhenStepKeysAreDuplicated()
    {
        var firstStep = StepDefinition.CreateHttpRequest(
            Guid.NewGuid(),
            "duplicate-step",
            1,
            new HttpRequestStepConfiguration("https://api.example.com/customers"));
        var secondStep = StepDefinition.CreateTransform(
            Guid.NewGuid(),
            "shape-payload",
            2,
            new TransformStepConfiguration(
            [
                new TransformValueMapping("$.customerId", "$.id")
            ]));
        var definition = CreateWorkflowDefinition(stepDefinitions: [firstStep, secondStep]);
        SetProperty(secondStep, nameof(StepDefinition.Key), "duplicate-step");

        var validationResult = _validator.ValidateForActivation(definition);

        Assert.False(validationResult.IsValid);
        var error = Assert.Single(validationResult.Errors, e => e.Code == "workflow.steps.key.duplicate");
        Assert.Equal("stepDefinitions", error.Path);
        Assert.Contains("duplicate-step", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateForActivation_ReturnsError_WhenStepOrderHasGap()
    {
        var definition = CreateWorkflowDefinition(
            stepDefinitions:
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "fetch-customer",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/customers")),
                StepDefinition.CreateSendWebhook(
                    Guid.NewGuid(),
                    "notify-partner",
                    3,
                    new SendWebhookStepConfiguration("https://hooks.example.com/customer-created"))
            ]);

        var validationResult = _validator.ValidateForActivation(definition);

        Assert.False(validationResult.IsValid);
        var error = Assert.Single(validationResult.Errors, e => e.Code == "workflow.steps.order.sequence.invalid");
        Assert.Equal("stepDefinitions", error.Path);
        Assert.Equal("Step order values must start at 1 and increment by 1 with no gaps.", error.Message);
    }

    [Fact]
    public void ValidateForActivation_ReturnsError_WhenStepTypeIsUnknown()
    {
        var httpRequestStep = StepDefinition.CreateHttpRequest(
            Guid.NewGuid(),
            "fetch-customer",
            1,
            new HttpRequestStepConfiguration("https://api.example.com/customers"));
        var definition = CreateWorkflowDefinition(stepDefinitions: [httpRequestStep]);
        SetProperty(httpRequestStep, nameof(StepDefinition.Type), (StepType)999);

        var validationResult = _validator.ValidateForActivation(definition);

        Assert.False(validationResult.IsValid);
        var error = Assert.Single(validationResult.Errors, e => e.Code == "workflow.step.type.unknown");
        Assert.Equal("stepDefinitions[0].type", error.Path);
        Assert.Contains("999", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateForActivation_ReturnsError_WhenHttpRequestConfigurationIsInvalid()
    {
        var httpRequestStep = StepDefinition.CreateHttpRequest(
            Guid.NewGuid(),
            "fetch-customer",
            1,
            new HttpRequestStepConfiguration("https://api.example.com/customers"));
        var definition = CreateWorkflowDefinition(stepDefinitions: [httpRequestStep]);
        SetProperty(httpRequestStep.HttpRequestConfiguration!, nameof(HttpRequestStepConfiguration.Url), " ");

        var validationResult = _validator.ValidateForActivation(definition);

        Assert.False(validationResult.IsValid);
        var error = Assert.Single(validationResult.Errors, e => e.Code == "workflow.step.httpRequest.url.required");
        Assert.Equal("stepDefinitions[0].httpRequestConfiguration.url", error.Path);
    }

    [Fact]
    public void ValidateForActivation_ReturnsError_WhenHttpRequestTimeoutIsInvalid()
    {
        var httpRequestStep = StepDefinition.CreateHttpRequest(
            Guid.NewGuid(),
            "fetch-customer",
            1,
            new HttpRequestStepConfiguration("https://api.example.com/customers"));
        var definition = CreateWorkflowDefinition(stepDefinitions: [httpRequestStep]);
        SetProperty(httpRequestStep.HttpRequestConfiguration!, nameof(HttpRequestStepConfiguration.TimeoutSeconds), 0);

        var validationResult = _validator.ValidateForActivation(definition);

        Assert.False(validationResult.IsValid);
        var error = Assert.Single(validationResult.Errors, e => e.Code == "workflow.step.httpRequest.timeout.invalid");
        Assert.Equal("stepDefinitions[0].httpRequestConfiguration.timeoutSeconds", error.Path);
    }

    [Fact]
    public void ValidateForActivation_ReturnsError_WhenHttpRequestResponseClassificationOverlaps()
    {
        var httpRequestStep = StepDefinition.CreateHttpRequest(
            Guid.NewGuid(),
            "fetch-customer",
            1,
            new HttpRequestStepConfiguration("https://api.example.com/customers"));
        var definition = CreateWorkflowDefinition(stepDefinitions: [httpRequestStep]);
        var responseClassification = new HttpResponseClassificationConfiguration(
            successStatusCodes: [200],
            retryableStatusCodes: [503]);

        SetProperty(
            responseClassification,
            nameof(HttpResponseClassificationConfiguration.SuccessStatusCodes),
            new List<int> { 200, 409 });
        SetProperty(
            responseClassification,
            nameof(HttpResponseClassificationConfiguration.RetryableStatusCodes),
            new List<int> { 409, 503 });
        SetProperty(
            httpRequestStep.HttpRequestConfiguration!,
            nameof(HttpRequestStepConfiguration.ResponseClassification),
            responseClassification);

        var validationResult = _validator.ValidateForActivation(definition);

        Assert.False(validationResult.IsValid);
        var error = Assert.Single(
            validationResult.Errors,
            e => e.Code == "workflow.step.httpRequest.responseClassification.overlap.invalid");
        Assert.Equal("stepDefinitions[0].httpRequestConfiguration.responseClassification", error.Path);
    }

    [Fact]
    public void ValidateForActivation_ReturnsError_WhenWebhookSignatureValidationConfigurationIsInvalid()
    {
        var triggerDefinition = TriggerDefinition.CreateWebhook(
            Guid.NewGuid(),
            new WebhookTriggerConfiguration(
                "partner-events",
                "POST",
                new WebhookSignatureValidationConfiguration(
                    "X-StepTrail-Signature",
                    "partner-signing-secret",
                    WebhookSignatureAlgorithm.HmacSha256)));
        var definition = CreateWorkflowDefinition(triggerDefinition: triggerDefinition);

        SetProperty(
            triggerDefinition.WebhookConfiguration!.SignatureValidation!,
            nameof(WebhookSignatureValidationConfiguration.HeaderName),
            " ");

        var validationResult = _validator.ValidateForActivation(definition);

        Assert.False(validationResult.IsValid);
        var error = Assert.Single(
            validationResult.Errors,
            e => e.Code == "workflow.trigger.webhook.signature.headerName.required");
        Assert.Equal("triggerDefinition.webhookConfiguration.signatureValidation.headerName", error.Path);
    }

    [Fact]
    public void ValidateForActivation_ReturnsError_WhenWebhookInputMappingHasInvalidSourceRoot()
    {
        var triggerDefinition = TriggerDefinition.CreateWebhook(
            Guid.NewGuid(),
            new WebhookTriggerConfiguration(
                "partner-events",
                "POST",
                inputMappings:
                [
                    new WebhookInputMapping("eventId", "body.eventId")
                ]));
        var definition = CreateWorkflowDefinition(triggerDefinition: triggerDefinition);

        SetProperty(
            triggerDefinition.WebhookConfiguration!.InputMappings[0],
            nameof(WebhookInputMapping.SourcePath),
            "payload.eventId");

        var validationResult = _validator.ValidateForActivation(definition);

        Assert.False(validationResult.IsValid);
        var error = Assert.Single(
            validationResult.Errors,
            e => e.Code == "workflow.trigger.webhook.inputMapping.sourcePath.root.invalid");
        Assert.Equal("triggerDefinition.webhookConfiguration.inputMappings[0].sourcePath", error.Path);
    }

    [Fact]
    public void ValidateForActivation_ReturnsError_WhenWebhookIdempotencySourceRootIsInvalid()
    {
        var triggerDefinition = TriggerDefinition.CreateWebhook(
            Guid.NewGuid(),
            new WebhookTriggerConfiguration(
                "partner-events",
                "POST",
                idempotencyKeyExtraction: new WebhookIdempotencyKeyExtractionConfiguration("headers.x-delivery-id")));
        var definition = CreateWorkflowDefinition(triggerDefinition: triggerDefinition);

        SetProperty(
            triggerDefinition.WebhookConfiguration!.IdempotencyKeyExtraction!,
            nameof(WebhookIdempotencyKeyExtractionConfiguration.SourcePath),
            "query.delivery");

        var validationResult = _validator.ValidateForActivation(definition);

        Assert.False(validationResult.IsValid);
        var error = Assert.Single(
            validationResult.Errors,
            e => e.Code == "workflow.trigger.webhook.idempotency.sourcePath.root.invalid");
        Assert.Equal("triggerDefinition.webhookConfiguration.idempotencyKeyExtraction.sourcePath", error.Path);
    }

    [Fact]
    public void ValidateForActivation_ReturnsError_WhenScheduleCronExpressionIsInvalid()
    {
        var triggerDefinition = TriggerDefinition.CreateSchedule(
            Guid.NewGuid(),
            new ScheduleTriggerConfiguration("0 8 * * *"));
        var definition = CreateWorkflowDefinition(triggerDefinition: triggerDefinition);

        SetProperty(
            triggerDefinition.ScheduleConfiguration!,
            nameof(ScheduleTriggerConfiguration.CronExpression),
            "0 8 1 * 1");
        SetProperty(
            triggerDefinition.ScheduleConfiguration!,
            nameof(ScheduleTriggerConfiguration.IntervalSeconds),
            null);

        var validationResult = _validator.ValidateForActivation(definition);

        Assert.False(validationResult.IsValid);
        var error = Assert.Single(
            validationResult.Errors,
            e => e.Code == "workflow.trigger.schedule.cron.invalid");
        Assert.Equal("triggerDefinition.scheduleConfiguration.cronExpression", error.Path);
    }

    [Fact]
    public void ValidateForActivation_ReturnsError_WhenConditionalExpectedValueIsMissing()
    {
        var conditionalStep = StepDefinition.CreateConditional(
            Guid.NewGuid(),
            "check-status",
            1,
            new ConditionalStepConfiguration("$.status", ConditionalOperator.Equals, "ready"));
        var definition = CreateWorkflowDefinition(stepDefinitions: [conditionalStep]);

        SetProperty(
            conditionalStep.ConditionalConfiguration!,
            nameof(ConditionalStepConfiguration.ExpectedValue),
            " ");

        var validationResult = _validator.ValidateForActivation(definition);

        Assert.False(validationResult.IsValid);
        var error = Assert.Single(
            validationResult.Errors,
            e => e.Code == "workflow.step.conditional.expectedValue.required");
        Assert.Equal("stepDefinitions[0].conditionalConfiguration.expectedValue", error.Path);
    }

    [Fact]
    public void ValidateForActivation_ReturnsError_WhenTransformOperationTemplateIsMissing()
    {
        var transformStep = StepDefinition.CreateTransform(
            Guid.NewGuid(),
            "shape-payload",
            1,
            new TransformStepConfiguration(
            [
                new TransformValueMapping(
                    "$.displayName",
                    TransformValueOperation.CreateFormatString(
                        "Customer {0}",
                        ["{{input.customerId}}"]))
            ]));
        var definition = CreateWorkflowDefinition(stepDefinitions: [transformStep]);

        SetProperty(
            transformStep.TransformConfiguration!.Mappings[0].Operation!,
            nameof(TransformValueOperation.Template),
            " ");

        var validationResult = _validator.ValidateForActivation(definition);

        Assert.False(validationResult.IsValid);
        var error = Assert.Single(
            validationResult.Errors,
            e => e.Code == "workflow.step.transform.operation.format.template.required");
        Assert.Equal("stepDefinitions[0].transformConfiguration.mappings[0].operation.template", error.Path);
    }

    [Fact]
    public void ValidateForActivation_ReturnsError_WhenTransformTargetPathsConflict()
    {
        var definition = CreateWorkflowDefinition(
            stepDefinitions:
            [
                StepDefinition.CreateTransform(
                    Guid.NewGuid(),
                    "shape-payload",
                    1,
                    new TransformStepConfiguration(
                    [
                        new TransformValueMapping("$.status", "$.status"),
                        new TransformValueMapping("$.status.code", "$.code")
                    ]))
            ]);

        var validationResult = _validator.ValidateForActivation(definition);

        Assert.False(validationResult.IsValid);
        var error = Assert.Single(
            validationResult.Errors,
            e => e.Code == "workflow.step.transform.targetPath.conflict");
        Assert.Equal("stepDefinitions[0].transformConfiguration.mappings", error.Path);
    }

    [Fact]
    public void ValidateForActivation_ReturnsError_WhenSendWebhookMethodIsNotPost()
    {
        var webhookStep = StepDefinition.CreateSendWebhook(
            Guid.NewGuid(),
            "notify-partner",
            1,
            new SendWebhookStepConfiguration("https://hooks.example.com/customer-created"));
        var definition = CreateWorkflowDefinition(stepDefinitions: [webhookStep]);

        SetProperty(
            webhookStep.SendWebhookConfiguration!,
            nameof(SendWebhookStepConfiguration.Method),
            "PUT");

        var validationResult = _validator.ValidateForActivation(definition);

        Assert.False(validationResult.IsValid);
        var error = Assert.Single(
            validationResult.Errors,
            e => e.Code == "workflow.step.sendWebhook.method.unsupported");
        Assert.Equal("stepDefinitions[0].sendWebhookConfiguration.method", error.Path);
    }

    [Fact]
    public void ValidateForActivation_ReturnsError_WhenDelayTargetTimeExpressionIsInvalidLiteral()
    {
        var delayStep = StepDefinition.CreateDelay(
            Guid.NewGuid(),
            "wait-until-follow-up",
            1,
            new DelayStepConfiguration("2026-04-16T08:00:00Z"));
        var definition = CreateWorkflowDefinition(stepDefinitions: [delayStep]);

        SetProperty(
            delayStep.DelayConfiguration!,
            nameof(DelayStepConfiguration.TargetTimeExpression),
            "tomorrow morning");
        SetProperty(
            delayStep.DelayConfiguration!,
            nameof(DelayStepConfiguration.DelaySeconds),
            null);

        var validationResult = _validator.ValidateForActivation(definition);

        Assert.False(validationResult.IsValid);
        var error = Assert.Single(
            validationResult.Errors,
            e => e.Code == "workflow.step.delay.targetTimeExpression.invalid");
        Assert.Equal("stepDefinitions[0].delayConfiguration.targetTimeExpression", error.Path);
    }

    private static WorkflowDefinition CreateWorkflowDefinition(
        IReadOnlyList<StepDefinition>? stepDefinitions = null,
        TriggerDefinition? triggerDefinition = null)
    {
        var createdAtUtc = new DateTimeOffset(2026, 4, 14, 12, 0, 0, TimeSpan.Zero);
        var updatedAtUtc = createdAtUtc.AddMinutes(1);

        return new WorkflowDefinition(
            Guid.NewGuid(),
            "customer-sync",
            "Customer Sync",
            1,
            WorkflowDefinitionStatus.Active,
            triggerDefinition ?? TriggerDefinition.CreateManual(
                Guid.NewGuid(),
                new ManualTriggerConfiguration("ops-console")),
            stepDefinitions ?? [CreateDefaultStep()],
            createdAtUtc,
            updatedAtUtc,
            "Executable workflow definition used for validation tests.");
    }

    private static StepDefinition CreateDefaultStep() =>
        StepDefinition.CreateHttpRequest(
            Guid.NewGuid(),
            "fetch-customer",
            1,
            new HttpRequestStepConfiguration("https://api.example.com/customers"));

    private static void SetProperty(object target, string propertyName, object? value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"Property '{propertyName}' was not found on type '{target.GetType().Name}'.");

        property.SetValue(target, value);
    }

    private static void ClearBackingStepDefinitions(WorkflowDefinition definition)
    {
        var field = definition.GetType().GetField(
            "_stepDefinitions",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"Field '_stepDefinitions' was not found on type '{definition.GetType().Name}'.");

        var stepDefinitions = (List<StepDefinition>?)field.GetValue(definition)
            ?? throw new InvalidOperationException("Workflow definition step backing list was null.");

        stepDefinitions.Clear();
    }
}
