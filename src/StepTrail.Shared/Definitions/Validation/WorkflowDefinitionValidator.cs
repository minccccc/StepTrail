namespace StepTrail.Shared.Definitions;

public sealed class WorkflowDefinitionValidator : IWorkflowDefinitionValidator
{
    public WorkflowDefinitionValidationResult ValidateForActivation(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var validationResult = new WorkflowDefinitionValidationResult();

        ValidateTrigger(definition.TriggerDefinition, validationResult);
        ValidateSteps(definition.StepDefinitions, validationResult);

        return validationResult;
    }

    private static void ValidateTrigger(
        TriggerDefinition? triggerDefinition,
        WorkflowDefinitionValidationResult validationResult)
    {
        if (triggerDefinition is null)
        {
            validationResult.AddError(
                "workflow.trigger.required",
                "triggerDefinition",
                "Workflow definition must declare a trigger definition before it can be activated.");
            return;
        }

        var configuredCount =
            (triggerDefinition.WebhookConfiguration is null ? 0 : 1) +
            (triggerDefinition.ManualConfiguration is null ? 0 : 1) +
            (triggerDefinition.ApiConfiguration is null ? 0 : 1) +
            (triggerDefinition.ScheduleConfiguration is null ? 0 : 1);

        if (configuredCount != 1)
        {
            validationResult.AddError(
                "workflow.trigger.configuration.invalid",
                "triggerDefinition",
                "Trigger definition must contain exactly one type-specific configuration payload.");
        }

        if (!Enum.IsDefined(triggerDefinition.Type))
        {
            validationResult.AddError(
                "workflow.trigger.type.unknown",
                "triggerDefinition.type",
                $"Trigger type '{triggerDefinition.Type}' is not supported.");
            return;
        }

        switch (triggerDefinition.Type)
        {
            case TriggerType.Webhook:
                if (triggerDefinition.WebhookConfiguration is null)
                {
                    validationResult.AddError(
                        "workflow.trigger.configuration.required",
                        "triggerDefinition.webhookConfiguration",
                        "Webhook trigger definitions require webhook configuration.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(triggerDefinition.WebhookConfiguration.RouteKey))
                {
                    validationResult.AddError(
                        "workflow.trigger.webhook.routeKey.required",
                        "triggerDefinition.webhookConfiguration.routeKey",
                        "Webhook trigger route key must not be empty.");
                }

                if (string.IsNullOrWhiteSpace(triggerDefinition.WebhookConfiguration.HttpMethod))
                {
                    validationResult.AddError(
                        "workflow.trigger.webhook.httpMethod.required",
                        "triggerDefinition.webhookConfiguration.httpMethod",
                        "Webhook trigger HTTP method must not be empty.");
                }

                break;

            case TriggerType.Manual:
                if (triggerDefinition.ManualConfiguration is null)
                {
                    validationResult.AddError(
                        "workflow.trigger.configuration.required",
                        "triggerDefinition.manualConfiguration",
                        "Manual trigger definitions require manual configuration.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(triggerDefinition.ManualConfiguration.EntryPointKey))
                {
                    validationResult.AddError(
                        "workflow.trigger.manual.entryPointKey.required",
                        "triggerDefinition.manualConfiguration.entryPointKey",
                        "Manual trigger entry point key must not be empty.");
                }

                break;

            case TriggerType.Api:
                if (triggerDefinition.ApiConfiguration is null)
                {
                    validationResult.AddError(
                        "workflow.trigger.configuration.required",
                        "triggerDefinition.apiConfiguration",
                        "API trigger definitions require API configuration.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(triggerDefinition.ApiConfiguration.OperationKey))
                {
                    validationResult.AddError(
                        "workflow.trigger.api.operationKey.required",
                        "triggerDefinition.apiConfiguration.operationKey",
                        "API trigger operation key must not be empty.");
                }

                break;

            case TriggerType.Schedule:
                if (triggerDefinition.ScheduleConfiguration is null)
                {
                    validationResult.AddError(
                        "workflow.trigger.configuration.required",
                        "triggerDefinition.scheduleConfiguration",
                        "Schedule trigger definitions require schedule configuration.");
                    return;
                }

                if (triggerDefinition.ScheduleConfiguration.IntervalSeconds < 1)
                {
                    validationResult.AddError(
                        "workflow.trigger.schedule.interval.invalid",
                        "triggerDefinition.scheduleConfiguration.intervalSeconds",
                        "Schedule trigger interval must be 1 second or greater.");
                }

                break;
        }
    }

    private static void ValidateSteps(
        IReadOnlyList<StepDefinition>? stepDefinitions,
        WorkflowDefinitionValidationResult validationResult)
    {
        if (stepDefinitions is null || stepDefinitions.Count == 0)
        {
            validationResult.AddError(
                "workflow.steps.required",
                "stepDefinitions",
                "Workflow definition must contain at least one step before it can be activated.");
            return;
        }

        var duplicateKeys = stepDefinitions
            .GroupBy(step => step.Key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        if (duplicateKeys.Count > 0)
        {
            validationResult.AddError(
                "workflow.steps.key.duplicate",
                "stepDefinitions",
                $"Workflow definition contains duplicate step keys: {string.Join(", ", duplicateKeys)}.");
        }

        var duplicateOrders = stepDefinitions
            .GroupBy(step => step.Order)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(order => order)
            .ToList();

        if (duplicateOrders.Count > 0)
        {
            validationResult.AddError(
                "workflow.steps.order.duplicate",
                "stepDefinitions",
                $"Workflow definition contains duplicate step orders: {string.Join(", ", duplicateOrders)}.");
        }

        var orderedValues = stepDefinitions
            .Select(step => step.Order)
            .OrderBy(order => order)
            .ToList();

        if (orderedValues.Any(order => order < 1))
        {
            validationResult.AddError(
                "workflow.steps.order.invalid",
                "stepDefinitions",
                "Step order values must be 1 or greater.");
        }
        else if (duplicateOrders.Count == 0
              && orderedValues
                  .Where((order, index) => order != index + 1)
                  .Any())
        {
            validationResult.AddError(
                "workflow.steps.order.sequence.invalid",
                "stepDefinitions",
                "Step order values must start at 1 and increment by 1 with no gaps.");
        }

        for (var index = 0; index < stepDefinitions.Count; index++)
            ValidateStep(stepDefinitions[index], index, validationResult);
    }

    private static void ValidateStep(
        StepDefinition stepDefinition,
        int index,
        WorkflowDefinitionValidationResult validationResult)
    {
        var stepPath = $"stepDefinitions[{index}]";

        if (string.IsNullOrWhiteSpace(stepDefinition.Key))
        {
            validationResult.AddError(
                "workflow.step.key.required",
                $"{stepPath}.key",
                "Step key must not be empty.");
        }

        if (!Enum.IsDefined(stepDefinition.Type))
        {
            validationResult.AddError(
                "workflow.step.type.unknown",
                $"{stepPath}.type",
                $"Step type '{stepDefinition.Type}' is not supported.");
            return;
        }

        var configuredCount =
            (stepDefinition.HttpRequestConfiguration is null ? 0 : 1) +
            (stepDefinition.TransformConfiguration is null ? 0 : 1) +
            (stepDefinition.ConditionalConfiguration is null ? 0 : 1) +
            (stepDefinition.DelayConfiguration is null ? 0 : 1) +
            (stepDefinition.SendWebhookConfiguration is null ? 0 : 1);

        if (configuredCount != 1)
        {
            validationResult.AddError(
                "workflow.step.configuration.invalid",
                $"{stepPath}.configuration",
                "Step definition must contain exactly one type-specific configuration payload.");
        }

        switch (stepDefinition.Type)
        {
            case StepType.HttpRequest:
                if (stepDefinition.HttpRequestConfiguration is null)
                {
                    validationResult.AddError(
                        "workflow.step.configuration.required",
                        $"{stepPath}.httpRequestConfiguration",
                        "HTTP request steps require HTTP request configuration.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(stepDefinition.HttpRequestConfiguration.Url))
                {
                    validationResult.AddError(
                        "workflow.step.httpRequest.url.required",
                        $"{stepPath}.httpRequestConfiguration.url",
                        "HTTP request step URL must not be empty.");
                }

                if (string.IsNullOrWhiteSpace(stepDefinition.HttpRequestConfiguration.Method))
                {
                    validationResult.AddError(
                        "workflow.step.httpRequest.method.required",
                        $"{stepPath}.httpRequestConfiguration.method",
                        "HTTP request step method must not be empty.");
                }

                break;

            case StepType.Transform:
                if (stepDefinition.TransformConfiguration is null)
                {
                    validationResult.AddError(
                        "workflow.step.configuration.required",
                        $"{stepPath}.transformConfiguration",
                        "Transform steps require transform configuration.");
                    return;
                }

                if (stepDefinition.TransformConfiguration.Mappings.Count == 0)
                {
                    validationResult.AddError(
                        "workflow.step.transform.mappings.required",
                        $"{stepPath}.transformConfiguration.mappings",
                        "Transform step configuration must contain at least one mapping.");
                }

                break;

            case StepType.Conditional:
                if (stepDefinition.ConditionalConfiguration is null)
                {
                    validationResult.AddError(
                        "workflow.step.configuration.required",
                        $"{stepPath}.conditionalConfiguration",
                        "Conditional steps require conditional configuration.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(stepDefinition.ConditionalConfiguration.ConditionExpression))
                {
                    validationResult.AddError(
                        "workflow.step.conditional.condition.required",
                        $"{stepPath}.conditionalConfiguration.conditionExpression",
                        "Conditional step expression must not be empty.");
                }

                break;

            case StepType.Delay:
                if (stepDefinition.DelayConfiguration is null)
                {
                    validationResult.AddError(
                        "workflow.step.configuration.required",
                        $"{stepPath}.delayConfiguration",
                        "Delay steps require delay configuration.");
                    return;
                }

                if (stepDefinition.DelayConfiguration.DelaySeconds < 1)
                {
                    validationResult.AddError(
                        "workflow.step.delay.seconds.invalid",
                        $"{stepPath}.delayConfiguration.delaySeconds",
                        "Delay step duration must be 1 second or greater.");
                }

                break;

            case StepType.SendWebhook:
                if (stepDefinition.SendWebhookConfiguration is null)
                {
                    validationResult.AddError(
                        "workflow.step.configuration.required",
                        $"{stepPath}.sendWebhookConfiguration",
                        "Send webhook steps require webhook configuration.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(stepDefinition.SendWebhookConfiguration.WebhookUrl))
                {
                    validationResult.AddError(
                        "workflow.step.sendWebhook.url.required",
                        $"{stepPath}.sendWebhookConfiguration.webhookUrl",
                        "Send webhook URL must not be empty.");
                }

                if (string.IsNullOrWhiteSpace(stepDefinition.SendWebhookConfiguration.Method))
                {
                    validationResult.AddError(
                        "workflow.step.sendWebhook.method.required",
                        $"{stepPath}.sendWebhookConfiguration.method",
                        "Send webhook method must not be empty.");
                }

                break;
        }
    }
}
