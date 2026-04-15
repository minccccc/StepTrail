using StepTrail.Shared.Runtime.Scheduling;

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

                var signatureValidation = triggerDefinition.WebhookConfiguration.SignatureValidation;
                if (signatureValidation is not null)
                {
                    if (string.IsNullOrWhiteSpace(signatureValidation.HeaderName))
                    {
                        validationResult.AddError(
                            "workflow.trigger.webhook.signature.headerName.required",
                            "triggerDefinition.webhookConfiguration.signatureValidation.headerName",
                            "Webhook signature validation header name must not be empty.");
                    }

                    if (string.IsNullOrWhiteSpace(signatureValidation.SecretName))
                    {
                        validationResult.AddError(
                            "workflow.trigger.webhook.signature.secretName.required",
                            "triggerDefinition.webhookConfiguration.signatureValidation.secretName",
                            "Webhook signature validation secret name must not be empty.");
                    }

                    if (!Enum.IsDefined(signatureValidation.Algorithm))
                    {
                        validationResult.AddError(
                            "workflow.trigger.webhook.signature.algorithm.unknown",
                            "triggerDefinition.webhookConfiguration.signatureValidation.algorithm",
                            $"Webhook signature algorithm '{signatureValidation.Algorithm}' is not supported.");
                    }
                }

                ValidateWebhookInputMappings(
                    triggerDefinition.WebhookConfiguration.InputMappings,
                    validationResult);
                ValidateWebhookIdempotencyKeyExtraction(
                    triggerDefinition.WebhookConfiguration.IdempotencyKeyExtraction,
                    validationResult);

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

                var intervalSeconds = triggerDefinition.ScheduleConfiguration.IntervalSeconds;
                var cronExpression = triggerDefinition.ScheduleConfiguration.CronExpression;

                if ((intervalSeconds.HasValue ? 1 : 0) + (string.IsNullOrWhiteSpace(cronExpression) ? 0 : 1) != 1)
                {
                    validationResult.AddError(
                        "workflow.trigger.schedule.mode.invalid",
                        "triggerDefinition.scheduleConfiguration",
                        "Schedule trigger must define exactly one mode: intervalSeconds or cronExpression.");
                    break;
                }

                if (intervalSeconds.HasValue && intervalSeconds.Value < 1)
                {
                    validationResult.AddError(
                        "workflow.trigger.schedule.interval.invalid",
                        "triggerDefinition.scheduleConfiguration.intervalSeconds",
                        "Schedule trigger interval must be 1 second or greater.");
                }

                if (!string.IsNullOrWhiteSpace(cronExpression)
                    && !SimpleCronExpression.TryParse(cronExpression, out _, out var cronError))
                {
                    validationResult.AddError(
                        "workflow.trigger.schedule.cron.invalid",
                        "triggerDefinition.scheduleConfiguration.cronExpression",
                        $"Schedule trigger cron expression is invalid: {cronError}");
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

    private static void ValidateWebhookInputMappings(
        IReadOnlyList<WebhookInputMapping> inputMappings,
        WorkflowDefinitionValidationResult validationResult)
    {
        if (inputMappings.Count == 0)
            return;

        var duplicateTargetPaths = inputMappings
            .GroupBy(mapping => mapping.TargetPath, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        if (duplicateTargetPaths.Count > 0)
        {
            validationResult.AddError(
                "workflow.trigger.webhook.inputMapping.targetPath.duplicate",
                "triggerDefinition.webhookConfiguration.inputMappings",
                $"Webhook input mappings contain duplicate target paths: {string.Join(", ", duplicateTargetPaths)}.");
        }

        for (var index = 0; index < inputMappings.Count; index++)
        {
            var mapping = inputMappings[index];
            var mappingPath = $"triggerDefinition.webhookConfiguration.inputMappings[{index}]";

            if (string.IsNullOrWhiteSpace(mapping.TargetPath))
            {
                validationResult.AddError(
                    "workflow.trigger.webhook.inputMapping.targetPath.required",
                    $"{mappingPath}.targetPath",
                    "Webhook input mapping target path must not be empty.");
            }

            if (string.IsNullOrWhiteSpace(mapping.SourcePath))
            {
                validationResult.AddError(
                    "workflow.trigger.webhook.inputMapping.sourcePath.required",
                    $"{mappingPath}.sourcePath",
                    "Webhook input mapping source path must not be empty.");
                continue;
            }

            var sourceSegments = mapping.SourcePath
                .Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (sourceSegments.Length == 0)
            {
                validationResult.AddError(
                    "workflow.trigger.webhook.inputMapping.sourcePath.invalid",
                    $"{mappingPath}.sourcePath",
                    "Webhook input mapping source path must not be empty.");
                continue;
            }

            var root = sourceSegments[0];
            if (!string.Equals(root, "body", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(root, "headers", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(root, "query", StringComparison.OrdinalIgnoreCase))
            {
                validationResult.AddError(
                    "workflow.trigger.webhook.inputMapping.sourcePath.root.invalid",
                    $"{mappingPath}.sourcePath",
                    "Webhook input mapping source path must start with 'body', 'headers', or 'query'.");
            }
            else if (!string.Equals(root, "body", StringComparison.OrdinalIgnoreCase)
                     && sourceSegments.Length < 2)
            {
                validationResult.AddError(
                    "workflow.trigger.webhook.inputMapping.sourcePath.key.required",
                    $"{mappingPath}.sourcePath",
                    "Header and query mappings must specify a source key after the root, for example 'headers.x-request-id'.");
            }
        }
    }

    private static void ValidateWebhookIdempotencyKeyExtraction(
        WebhookIdempotencyKeyExtractionConfiguration? idempotencyKeyExtraction,
        WorkflowDefinitionValidationResult validationResult)
    {
        if (idempotencyKeyExtraction is null)
            return;

        if (string.IsNullOrWhiteSpace(idempotencyKeyExtraction.SourcePath))
        {
            validationResult.AddError(
                "workflow.trigger.webhook.idempotency.sourcePath.required",
                "triggerDefinition.webhookConfiguration.idempotencyKeyExtraction.sourcePath",
                "Webhook idempotency key source path must not be empty.");
            return;
        }

        var sourceSegments = idempotencyKeyExtraction.SourcePath
            .Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (sourceSegments.Length < 2)
        {
            validationResult.AddError(
                "workflow.trigger.webhook.idempotency.sourcePath.invalid",
                "triggerDefinition.webhookConfiguration.idempotencyKeyExtraction.sourcePath",
                "Webhook idempotency key source path must include a root and field path, for example 'headers.x-idempotency-key' or 'body.eventId'.");
            return;
        }

        var root = sourceSegments[0];
        if (!string.Equals(root, "body", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(root, "headers", StringComparison.OrdinalIgnoreCase))
        {
            validationResult.AddError(
                "workflow.trigger.webhook.idempotency.sourcePath.root.invalid",
                "triggerDefinition.webhookConfiguration.idempotencyKeyExtraction.sourcePath",
                "Webhook idempotency key source path must start with 'body' or 'headers'.");
        }
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

                if (stepDefinition.HttpRequestConfiguration.TimeoutSeconds is < 1)
                {
                    validationResult.AddError(
                        "workflow.step.httpRequest.timeout.invalid",
                        $"{stepPath}.httpRequestConfiguration.timeoutSeconds",
                        "HTTP request step timeout must be 1 second or greater when specified.");
                }

                ValidateHttpResponseClassificationConfiguration(
                    stepDefinition.HttpRequestConfiguration.ResponseClassification,
                    $"{stepPath}.httpRequestConfiguration.responseClassification",
                    validationResult);

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

                ValidateTransformMappings(
                    stepDefinition.TransformConfiguration.Mappings,
                    $"{stepPath}.transformConfiguration.mappings",
                    validationResult);

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

                if (string.IsNullOrWhiteSpace(stepDefinition.ConditionalConfiguration.SourcePath))
                {
                    validationResult.AddError(
                        "workflow.step.conditional.sourcePath.required",
                        $"{stepPath}.conditionalConfiguration.sourcePath",
                        "Conditional step source path must not be empty.");
                }

                if (!Enum.IsDefined(stepDefinition.ConditionalConfiguration.Operator))
                {
                    validationResult.AddError(
                        "workflow.step.conditional.operator.unknown",
                        $"{stepPath}.conditionalConfiguration.operator",
                        $"Conditional operator '{stepDefinition.ConditionalConfiguration.Operator}' is not supported.");
                }
                else if (stepDefinition.ConditionalConfiguration.Operator is ConditionalOperator.Equals or ConditionalOperator.NotEquals)
                {
                    if (string.IsNullOrWhiteSpace(stepDefinition.ConditionalConfiguration.ExpectedValue))
                    {
                        validationResult.AddError(
                            "workflow.step.conditional.expectedValue.required",
                            $"{stepPath}.conditionalConfiguration.expectedValue",
                            "Conditional equals/not-equals operators require expectedValue.");
                    }
                }
                else if (stepDefinition.ConditionalConfiguration.ExpectedValue is not null)
                {
                    validationResult.AddError(
                        "workflow.step.conditional.expectedValue.unexpected",
                        $"{stepPath}.conditionalConfiguration.expectedValue",
                        "Conditional exists/not-exists operators must not define expectedValue.");
                }

                if (!Enum.IsDefined(stepDefinition.ConditionalConfiguration.FalseOutcome))
                {
                    validationResult.AddError(
                        "workflow.step.conditional.falseOutcome.unknown",
                        $"{stepPath}.conditionalConfiguration.falseOutcome",
                        $"Conditional false outcome '{stepDefinition.ConditionalConfiguration.FalseOutcome}' is not supported.");
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

                if (stepDefinition.DelayConfiguration.DelaySeconds.HasValue
                    && !string.IsNullOrWhiteSpace(stepDefinition.DelayConfiguration.TargetTimeExpression))
                {
                    validationResult.AddError(
                        "workflow.step.delay.mode.conflict",
                        $"{stepPath}.delayConfiguration",
                        "Delay steps must define either a fixed duration or a target time expression, but not both.");
                    return;
                }

                if (!stepDefinition.DelayConfiguration.DelaySeconds.HasValue
                    && string.IsNullOrWhiteSpace(stepDefinition.DelayConfiguration.TargetTimeExpression))
                {
                    validationResult.AddError(
                        "workflow.step.delay.mode.required",
                        $"{stepPath}.delayConfiguration",
                        "Delay steps must define either a fixed duration or a target time expression.");
                    return;
                }

                if (stepDefinition.DelayConfiguration.DelaySeconds.HasValue
                    && stepDefinition.DelayConfiguration.DelaySeconds.Value < 1)
                {
                    validationResult.AddError(
                        "workflow.step.delay.seconds.invalid",
                        $"{stepPath}.delayConfiguration.delaySeconds",
                        "Delay step duration must be 1 second or greater.");
                }

                if (!string.IsNullOrWhiteSpace(stepDefinition.DelayConfiguration.TargetTimeExpression))
                {
                    var expression = stepDefinition.DelayConfiguration.TargetTimeExpression!;
                    var isDynamicReference = expression.StartsWith("{{", StringComparison.Ordinal)
                        || expression.StartsWith("$", StringComparison.Ordinal);

                    if (!isDynamicReference && !DelayTimestampParser.TryParseUtcTimestamp(expression, out _))
                    {
                        validationResult.AddError(
                            "workflow.step.delay.targetTimeExpression.invalid",
                            $"{stepPath}.delayConfiguration.targetTimeExpression",
                            "Delay-until target time expression must be a valid UTC timestamp or a placeholder-based reference.");
                    }
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

                if (!string.IsNullOrWhiteSpace(stepDefinition.SendWebhookConfiguration.Method)
                    && !string.Equals(stepDefinition.SendWebhookConfiguration.Method, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    validationResult.AddError(
                        "workflow.step.sendWebhook.method.unsupported",
                        $"{stepPath}.sendWebhookConfiguration.method",
                        "Send webhook steps currently support only POST.");
                }

                if (stepDefinition.SendWebhookConfiguration.TimeoutSeconds.HasValue
                    && stepDefinition.SendWebhookConfiguration.TimeoutSeconds.Value < 1)
                {
                    validationResult.AddError(
                        "workflow.step.sendWebhook.timeout.invalid",
                        $"{stepPath}.sendWebhookConfiguration.timeoutSeconds",
                        "Send webhook timeout must be 1 second or greater.");
                }

                break;
        }
    }

    private static void ValidateHttpResponseClassificationConfiguration(
        HttpResponseClassificationConfiguration? configuration,
        string pathPrefix,
        WorkflowDefinitionValidationResult validationResult)
    {
        if (configuration is null)
            return;

        foreach (var statusCode in configuration.SuccessStatusCodes)
        {
            if (statusCode < 100 || statusCode > 599)
            {
                validationResult.AddError(
                    "workflow.step.httpRequest.responseClassification.successStatusCodes.invalid",
                    $"{pathPrefix}.successStatusCodes",
                    $"HTTP success status code '{statusCode}' must be between 100 and 599.");
            }
        }

        foreach (var statusCode in configuration.RetryableStatusCodes)
        {
            if (statusCode < 100 || statusCode > 599)
            {
                validationResult.AddError(
                    "workflow.step.httpRequest.responseClassification.retryableStatusCodes.invalid",
                    $"{pathPrefix}.retryableStatusCodes",
                    $"HTTP retryable status code '{statusCode}' must be between 100 and 599.");
            }
        }

        var overlappingStatusCodes = configuration.SuccessStatusCodes
            .Intersect(configuration.RetryableStatusCodes)
            .OrderBy(code => code)
            .ToList();

        if (overlappingStatusCodes.Count > 0)
        {
            validationResult.AddError(
                "workflow.step.httpRequest.responseClassification.overlap.invalid",
                pathPrefix,
                $"HTTP response classification cannot mark the same status code as both success and retryable: {string.Join(", ", overlappingStatusCodes)}.");
        }
    }

    private static void ValidateTransformMappings(
        IReadOnlyList<TransformValueMapping> mappings,
        string pathPrefix,
        WorkflowDefinitionValidationResult validationResult)
    {
        var normalizedTargetPaths = new List<string>();

        for (var index = 0; index < mappings.Count; index++)
        {
            var mapping = mappings[index];
            var mappingPath = $"{pathPrefix}[{index}]";

            try
            {
                normalizedTargetPaths.Add(mapping.NormalizedTargetPath);
            }
            catch (ArgumentException ex)
            {
                validationResult.AddError(
                    "workflow.step.transform.targetPath.invalid",
                    $"{mappingPath}.targetPath",
                    ex.Message);
            }

            var hasSourcePath = !string.IsNullOrWhiteSpace(mapping.SourcePath);
            var hasOperation = mapping.Operation is not null;

            if (hasSourcePath == hasOperation)
            {
                validationResult.AddError(
                    "workflow.step.transform.mapping.mode.invalid",
                    mappingPath,
                    "Transform mappings must define exactly one of sourcePath or operation.");
                continue;
            }

            if (hasSourcePath)
                continue;

            ValidateTransformOperation(mapping.Operation!, $"{mappingPath}.operation", validationResult);
        }

        var duplicateTargetPaths = normalizedTargetPaths
            .GroupBy(path => path, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        if (duplicateTargetPaths.Count > 0)
        {
            validationResult.AddError(
                "workflow.step.transform.targetPath.duplicate",
                pathPrefix,
                $"Transform mappings contain duplicate target paths: {string.Join(", ", duplicateTargetPaths)}.");
        }

        var conflictingTargetPaths = normalizedTargetPaths
            .OrderBy(path => path, StringComparer.Ordinal)
            .SelectMany((path, index) => normalizedTargetPaths.Skip(index + 1)
                .Where(other => other.StartsWith(path + ".", StringComparison.Ordinal)
                             || path.StartsWith(other + ".", StringComparison.Ordinal))
                .Select(other => $"{path} <-> {other}"))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (conflictingTargetPaths.Count > 0)
        {
            validationResult.AddError(
                "workflow.step.transform.targetPath.conflict",
                pathPrefix,
                $"Transform mappings contain conflicting target paths that overlap as parent/child objects: {string.Join(", ", conflictingTargetPaths)}.");
        }
    }

    private static void ValidateTransformOperation(
        TransformValueOperation operation,
        string pathPrefix,
        WorkflowDefinitionValidationResult validationResult)
    {
        if (!Enum.IsDefined(operation.Type))
        {
            validationResult.AddError(
                "workflow.step.transform.operation.type.unknown",
                $"{pathPrefix}.type",
                $"Transform operation type '{operation.Type}' is not supported.");
            return;
        }

        switch (operation.Type)
        {
            case TransformOperationType.DefaultValue:
                if (string.IsNullOrWhiteSpace(operation.SourcePath))
                {
                    validationResult.AddError(
                        "workflow.step.transform.operation.default.sourcePath.required",
                        $"{pathPrefix}.sourcePath",
                        "Default value transform operations require sourcePath.");
                }

                if (operation.DefaultValue is null)
                {
                    validationResult.AddError(
                        "workflow.step.transform.operation.default.defaultValue.required",
                        $"{pathPrefix}.defaultValue",
                        "Default value transform operations require defaultValue.");
                }

                break;

            case TransformOperationType.Concatenate:
                if (operation.Parts.Count == 0)
                {
                    validationResult.AddError(
                        "workflow.step.transform.operation.concatenate.parts.required",
                        $"{pathPrefix}.parts",
                        "Concatenate transform operations require at least one part.");
                }

                break;

            case TransformOperationType.FormatString:
                if (string.IsNullOrWhiteSpace(operation.Template))
                {
                    validationResult.AddError(
                        "workflow.step.transform.operation.format.template.required",
                        $"{pathPrefix}.template",
                        "Format string transform operations require template.");
                }

                if (operation.Arguments.Count == 0)
                {
                    validationResult.AddError(
                        "workflow.step.transform.operation.format.arguments.required",
                        $"{pathPrefix}.arguments",
                        "Format string transform operations require at least one argument.");
                }

                break;
        }
    }
}
