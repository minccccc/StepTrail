using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using StepTrail.Api.Models;
using StepTrail.Shared;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Definitions.Persistence;
using StepTrail.Shared.Runtime.AvailableFields;
using StepTrail.Shared.Telemetry;
using StepTrail.Shared.Workflows;

namespace StepTrail.Api.Endpoints;

public static class DefinitionEndpoints
{
    public static RouteGroupBuilder MapDefinitionEndpoints(this RouteGroupBuilder ops)
    {
        // ── Available fields ─────────────────────────────────────────────────────────

        ops.MapGet("/workflow-definitions/{key}/steps/{stepKey}/available-fields", async (
            string key,
            string stepKey,
            int? version,
            IWorkflowDefinitionRepository repository,
            StepTrailDbContext db,
            CancellationToken ct) =>
        {
            var definition = version.HasValue
                ? await repository.GetByKeyAndVersionAsync(key, version.Value, ct)
                : await repository.GetActiveByKeyAsync(key, ct);

            if (definition is null)
            {
                var versionSuffix = version.HasValue ? $" (version {version.Value})" : " (active)";
                return Results.NotFound(new { error = $"No workflow definition found for key '{key}'{versionSuffix}." });
            }

            var secretNames = await db.WorkflowSecrets
                .OrderBy(s => s.Name)
                .Select(s => s.Name)
                .ToListAsync(ct);

            try
            {
                var response = AvailableFieldsService.GetAvailableFields(definition, stepKey, secretNames);
                return Results.Ok(response);
            }
            catch (ArgumentException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        // ── GET /workflows (registry) ────────────────────────────────────────────────

        ops.MapGet("/workflows", (IWorkflowRegistry registry) =>
        {
            var workflows = registry.GetAll().Select(w => new
            {
                w.Key,
                w.Version,
                w.Name,
                w.Description,
                TriggerType = w.TriggerType.ToString(),
                w.RecurrenceIntervalSeconds,
                Steps = w.Steps
                    .OrderBy(s => s.Order)
                    .Select(s => new
                    {
                        s.Order,
                        s.StepKey,
                        s.StepType,
                        s.MaxAttempts,
                        s.RetryDelaySeconds,
                        s.TimeoutSeconds,
                        s.Config
                    })
            });
            return Results.Ok(workflows);
        });

        // ── GET /workflow-definitions ────────────────────────────────────────────────

        ops.MapGet("/workflow-definitions", async (
            StepTrailDbContext db,
            CancellationToken ct) =>
        {
            var definitions = await db.ExecutableWorkflowDefinitions
                .Include(d => d.TriggerDefinition)
                .Include(d => d.StepDefinitions)
                .AsNoTracking()
                .OrderBy(d => d.Key)
                .ThenByDescending(d => d.Version)
                .ToListAsync(ct);

            var result = definitions.Select(d => new WorkflowDefinitionSummary
            {
                Id = d.Id,
                Key = d.Key,
                Name = d.Name,
                Version = d.Version,
                Status = d.Status.ToString(),
                TriggerType = d.TriggerDefinition?.Type.ToString(),
                Description = d.Description,
                SourceTemplateKey = d.SourceTemplateKey,
                SourceTemplateVersion = d.SourceTemplateVersion,
                StepCount = d.StepDefinitions.Count,
                Steps = d.StepDefinitions
                    .OrderBy(s => s.Order)
                    .Select(s => new WorkflowDefinitionStepSummary
                    {
                        Key = s.Key,
                        Type = s.Type.ToString(),
                        Order = s.Order
                    })
                    .ToList(),
                CreatedAtUtc = d.CreatedAtUtc,
                UpdatedAtUtc = d.UpdatedAtUtc
            });

            return Results.Ok(result);
        });

        // ── POST from-descriptor ─────────────────────────────────────────────────────

        ops.MapPost("/workflow-definitions/from-descriptor", async (
            CreateFromDescriptorRequest request,
            IWorkflowRegistry registry,
            IWorkflowDefinitionRepository repository,
            TelemetryService telemetry,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "Name is required." });
            if (string.IsNullOrWhiteSpace(request.Key))
                return Results.BadRequest(new { error = "Key is required." });
            if (!IsValidWorkflowKey(request.Key.Trim()))
                return Results.BadRequest(new { error = "Key must contain only lowercase letters, numbers, and hyphens." });

            var descriptor = registry.Find(request.DescriptorKey, request.DescriptorVersion)
                            ?? registry.FindLatest(request.DescriptorKey);
            if (descriptor is null)
                return Results.NotFound(new { error = $"Template descriptor '{request.DescriptorKey}' not found." });

            // Use template's trigger type, allow explicit override via request
            if (!Enum.TryParse<TriggerType>(
                    string.IsNullOrWhiteSpace(request.TriggerType)
                        ? descriptor.TriggerType.ToString()
                        : request.TriggerType,
                    ignoreCase: true, out var triggerType))
                return Results.BadRequest(new { error = $"Invalid trigger type '{request.TriggerType}'." });

            var now = DateTimeOffset.UtcNow;

            var trigger = CreateDefaultTrigger(triggerType, request.Key.Trim());

            var steps = descriptor.Steps
                .OrderBy(s => s.Order)
                .Select(s =>
                {
                    var step = CreateDefaultStepDefinition(s.StepKey, s.Order, s.StepType);
                    // Carry over retry/timeout settings from the descriptor
                    RetryPolicy? policy = s.MaxAttempts > 1
                        ? new RetryPolicy(s.MaxAttempts, s.RetryDelaySeconds, BackoffStrategy.Fixed)
                        : null;
                    if (policy is not null)
                    {
                        // Rebuild with the descriptor's retry policy
                        step = step.Type switch
                        {
                            StepType.HttpRequest => StepDefinition.CreateHttpRequest(step.Id, step.Key, step.Order, step.HttpRequestConfiguration!, retryPolicy: policy),
                            StepType.SendWebhook => StepDefinition.CreateSendWebhook(step.Id, step.Key, step.Order, step.SendWebhookConfiguration!, retryPolicy: policy),
                            StepType.Transform => StepDefinition.CreateTransform(step.Id, step.Key, step.Order, step.TransformConfiguration!, retryPolicy: policy),
                            StepType.Conditional => StepDefinition.CreateConditional(step.Id, step.Key, step.Order, step.ConditionalConfiguration!, retryPolicy: policy),
                            StepType.Delay => StepDefinition.CreateDelay(step.Id, step.Key, step.Order, step.DelayConfiguration!, retryPolicy: policy),
                            _ => step
                        };
                    }
                    return step;
                })
                .ToList();

            var definition = new StepTrail.Shared.Definitions.WorkflowDefinition(
                Guid.NewGuid(), request.Key.Trim(), request.Name.Trim(), 1,
                WorkflowDefinitionStatus.Inactive, trigger, steps, now, now,
                descriptor.Description,
                sourceTemplateKey: descriptor.Key,
                sourceTemplateVersion: descriptor.Version);

            try
            {
                await repository.SaveNewVersionAsync(definition, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Conflict(new { error = ex.Message });
            }

            await telemetry.RecordAsync(TelemetryEvents.WorkflowCreatedFromTemplate, TelemetryEvents.Categories.Authoring, ct,
                workflowKey: definition.Key, workflowDefinitionId: definition.Id, status: definition.Status.ToString(), triggerType: triggerType.ToString(),
                metadata: new { descriptorKey = descriptor.Key, descriptorVersion = descriptor.Version, stepCount = steps.Count });

            return Results.Created(
                $"/workflow-definitions/{definition.Id}",
                new { id = definition.Id, key = definition.Key, name = definition.Name, status = definition.Status.ToString() });
        });

        // ── POST blank ───────────────────────────────────────────────────────────────

        ops.MapPost("/workflow-definitions/blank", async (
            CreateBlankDefinitionRequest request,
            IWorkflowDefinitionRepository repository,
            TelemetryService telemetry,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "Name is required." });
            if (string.IsNullOrWhiteSpace(request.Key))
                return Results.BadRequest(new { error = "Key is required." });
            if (!IsValidWorkflowKey(request.Key.Trim()))
                return Results.BadRequest(new { error = "Key must contain only lowercase letters, numbers, and hyphens, and must start and end with a letter or number." });
            if (!Enum.TryParse<TriggerType>(
                    string.IsNullOrWhiteSpace(request.TriggerType) ? "Webhook" : request.TriggerType,
                    ignoreCase: true, out var triggerType))
                return Results.BadRequest(new { error = $"Invalid trigger type '{request.TriggerType}'. Supported: Webhook, Manual, Schedule." });

            var now = DateTimeOffset.UtcNow;

            var trigger = CreateDefaultTrigger(triggerType, request.Key.Trim());

            var definition = new StepTrail.Shared.Definitions.WorkflowDefinition(
                Guid.NewGuid(),
                request.Key.Trim(),
                request.Name.Trim(),
                1,
                WorkflowDefinitionStatus.Inactive,
                trigger,
                Array.Empty<StepDefinition>(),
                now,
                now,
                $"Created manually with {triggerType} trigger.");

            try
            {
                await repository.SaveNewVersionAsync(definition, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Conflict(new { error = ex.Message });
            }

            await telemetry.RecordAsync(TelemetryEvents.WorkflowCreatedBlank, TelemetryEvents.Categories.Authoring, ct,
                workflowKey: definition.Key, workflowDefinitionId: definition.Id, status: definition.Status.ToString(), triggerType: triggerType.ToString());

            return Results.Created(
                $"/workflow-definitions/{definition.Id}",
                new { id = definition.Id, key = definition.Key, name = definition.Name, status = definition.Status.ToString() });
        });

        // ── POST clone ───────────────────────────────────────────────────────────────

        ops.MapPost("/workflow-definitions/clone", async (
            CloneWorkflowDefinitionRequest request,
            IWorkflowDefinitionRepository repository,
            TelemetryService telemetry,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "Name is required." });
            if (string.IsNullOrWhiteSpace(request.Key))
                return Results.BadRequest(new { error = "Key is required." });
            if (!IsValidWorkflowKey(request.Key.Trim()))
                return Results.BadRequest(new { error = "Key must contain only lowercase letters, numbers, and hyphens, and must start and end with a letter or number." });

            var template = await repository.GetByIdAsync(request.TemplateId, ct);
            if (template is null)
                return Results.NotFound(new { error = $"Template definition '{request.TemplateId}' not found." });

            var now = DateTimeOffset.UtcNow;

            // Clone trigger with a fresh ID.
            var clonedTrigger = new TriggerDefinition(
                Guid.NewGuid(),
                template.TriggerDefinition.Type,
                template.TriggerDefinition.WebhookConfiguration,
                template.TriggerDefinition.ManualConfiguration,
                template.TriggerDefinition.ScheduleConfiguration);

            var cloned = new StepTrail.Shared.Definitions.WorkflowDefinition(
                Guid.NewGuid(),
                request.Key.Trim(),
                request.Name.Trim(),
                1,
                WorkflowDefinitionStatus.Inactive,
                clonedTrigger,
                template.StepDefinitions.Select(step => new StepDefinition(
                    Guid.NewGuid(),
                    step.Key,
                    step.Order,
                    step.Type,
                    step.HttpRequestConfiguration,
                    step.TransformConfiguration,
                    step.ConditionalConfiguration,
                    step.DelayConfiguration,
                    step.SendWebhookConfiguration,
                    step.RetryPolicyOverrideKey,
                    step.RetryPolicy)),
                now,
                now,
                template.Description);

            try
            {
                await repository.SaveNewVersionAsync(cloned, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Conflict(new { error = ex.Message });
            }

            await telemetry.RecordAsync(TelemetryEvents.WorkflowCloned, TelemetryEvents.Categories.Authoring, ct,
                workflowKey: cloned.Key, workflowDefinitionId: cloned.Id, status: cloned.Status.ToString(),
                metadata: new { clonedFromId = request.TemplateId, clonedFromKey = template.Key });

            return Results.Created(
                $"/workflow-definitions/{cloned.Id}",
                new { id = cloned.Id, key = cloned.Key, name = cloned.Name, status = cloned.Status.ToString() });
        });

        // ── GET by id ────────────────────────────────────────────────────────────────

        ops.MapGet("/workflow-definitions/{id:guid}", async (
            Guid id,
            IWorkflowDefinitionRepository repository,
            CancellationToken ct) =>
        {
            var definition = await repository.GetByIdAsync(id, ct);
            if (definition is null)
                return Results.NotFound(new { error = $"Workflow definition '{id}' not found." });

            return Results.Ok(WorkflowDefinitionDetailMapper.Map(definition));
        });

        // ── PUT trigger-type ─────────────────────────────────────────────────────────

        ops.MapPut("/workflow-definitions/{id:guid}/trigger-type", async (
            Guid id,
            ChangeTriggerTypeRequest request,
            IWorkflowDefinitionRepository repository,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<TriggerType>(request.TriggerType, ignoreCase: true, out var newTriggerType))
                return Results.BadRequest(new { error = $"Invalid trigger type '{request.TriggerType}'." });

            var definition = await repository.GetByIdAsync(id, ct);
            if (definition is null)
                return Results.NotFound(new { error = $"Workflow definition '{id}' not found." });

            if (definition.TriggerDefinition.Type == newTriggerType)
                return Results.Ok(new { id, triggerType = newTriggerType.ToString(), message = "Already set." });

            var newTrigger = CreateDefaultTrigger(newTriggerType, definition.Key);

            var now = DateTimeOffset.UtcNow;
            var updated = new StepTrail.Shared.Definitions.WorkflowDefinition(
                definition.Id, definition.Key, definition.Name, definition.Version,
                definition.Status, newTrigger, definition.StepDefinitions,
                definition.CreatedAtUtc, now, definition.Description,
                definition.SourceTemplateKey, definition.SourceTemplateVersion);

            try
            {
                await repository.UpdateAsync(updated, ct);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }

            return Results.Ok(new { id, triggerType = newTriggerType.ToString() });
        });

        // ── PUT trigger ──────────────────────────────────────────────────────────────

        ops.MapPut("/workflow-definitions/{id:guid}/trigger", async (
            Guid id,
            UpdateTriggerRequest request,
            IWorkflowDefinitionRepository repository,
            CancellationToken ct) =>
        {
            var definition = await repository.GetByIdAsync(id, ct);
            if (definition is null)
                return Results.NotFound(new { error = $"Workflow definition '{id}' not found." });

            TriggerDefinition newTrigger;
            try
            {
                newTrigger = definition.TriggerDefinition.Type switch
                {
                    TriggerType.Webhook => TriggerDefinition.CreateWebhook(
                        definition.TriggerDefinition.Id,
                        new WebhookTriggerConfiguration(
                            request.RouteKey ?? definition.TriggerDefinition.WebhookConfiguration!.RouteKey,
                            request.HttpMethod ?? "POST",
                            !string.IsNullOrWhiteSpace(request.SignatureHeaderName) && !string.IsNullOrWhiteSpace(request.SignatureSecretName)
                                ? new WebhookSignatureValidationConfiguration(
                                    request.SignatureHeaderName!,
                                    request.SignatureSecretName!,
                                    Enum.TryParse<WebhookSignatureAlgorithm>(request.SignatureAlgorithm, true, out var algo)
                                        ? algo : WebhookSignatureAlgorithm.HmacSha256,
                                    request.SignaturePrefix)
                                : null,
                            inputMappings: definition.TriggerDefinition.WebhookConfiguration!.InputMappings,
                            idempotencyKeyExtraction: !string.IsNullOrWhiteSpace(request.IdempotencyKeySourcePath)
                                ? new WebhookIdempotencyKeyExtractionConfiguration(request.IdempotencyKeySourcePath)
                                : null)),
                    TriggerType.Manual => TriggerDefinition.CreateManual(
                        definition.TriggerDefinition.Id,
                        new ManualTriggerConfiguration(
                            request.EntryPointKey ?? definition.TriggerDefinition.ManualConfiguration!.EntryPointKey)),
                    TriggerType.Schedule => TriggerDefinition.CreateSchedule(
                        definition.TriggerDefinition.Id,
                        !string.IsNullOrWhiteSpace(request.CronExpression)
                            ? new ScheduleTriggerConfiguration(request.CronExpression)
                            : new ScheduleTriggerConfiguration(request.IntervalSeconds ?? 60)),
                    _ => throw new InvalidOperationException($"Unsupported trigger type '{definition.TriggerDefinition.Type}'.")
                };
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var now = DateTimeOffset.UtcNow;
            var updated = new StepTrail.Shared.Definitions.WorkflowDefinition(
                definition.Id,
                definition.Key,
                definition.Name,
                definition.Version,
                definition.Status,
                newTrigger,
                definition.StepDefinitions,
                definition.CreatedAtUtc,
                now,
                definition.Description,
                definition.SourceTemplateKey, definition.SourceTemplateVersion);

            try
            {
                await repository.UpdateAsync(updated, ct);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }

            return Results.Ok(new { id, status = updated.Status.ToString() });
        });

        // ── POST steps (add) ─────────────────────────────────────────────────────────

        ops.MapPost("/workflow-definitions/{id:guid}/steps", async (
            Guid id,
            AddStepRequest request,
            IWorkflowDefinitionRepository repository,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.StepKey))
                return Results.BadRequest(new { error = "Step key is required." });
            if (string.IsNullOrWhiteSpace(request.StepType))
                return Results.BadRequest(new { error = "Step type is required." });

            var definition = await repository.GetByIdAsync(id, ct);
            if (definition is null)
                return Results.NotFound(new { error = $"Workflow definition '{id}' not found." });

            if (definition.StepDefinitions.Any(s => s.Key == request.StepKey.Trim()))
                return Results.Conflict(new { error = $"Step key '{request.StepKey}' already exists." });

            if (!IsSupportedStepType(request.StepType))
                return Results.BadRequest(new { error = $"Unsupported step type '{request.StepType}'. Supported: HttpRequest, SendWebhook, Transform, Conditional, Delay." });

            var nextOrder = definition.StepDefinitions.Count > 0
                ? definition.StepDefinitions.Max(s => s.Order) + 1
                : 1;

            StepDefinition newStep;
            try
            {
                newStep = CreateDefaultStepDefinition(request.StepKey.Trim(), nextOrder, request.StepType);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var allSteps = definition.StepDefinitions.Append(newStep);
            var now = DateTimeOffset.UtcNow;

            var updated = new StepTrail.Shared.Definitions.WorkflowDefinition(
                definition.Id, definition.Key, definition.Name, definition.Version,
                definition.Status, definition.TriggerDefinition, allSteps,
                definition.CreatedAtUtc, now, definition.Description,
                definition.SourceTemplateKey, definition.SourceTemplateVersion);

            try
            {
                await repository.UpdateAsync(updated, ct);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }

            return Results.Created(
                $"/workflow-definitions/{id}/steps/{newStep.Key}",
                new { id, stepKey = newStep.Key, stepType = newStep.Type.ToString(), order = newStep.Order });
        });

        // ── PUT steps/{key} ──────────────────────────────────────────────────────────

        ops.MapPut("/workflow-definitions/{id:guid}/steps/{stepKey}", async (
            Guid id,
            string stepKey,
            UpdateStepRequest request,
            IWorkflowDefinitionRepository repository,
            CancellationToken ct) =>
        {
            var definition = await repository.GetByIdAsync(id, ct);
            if (definition is null)
                return Results.NotFound(new { error = $"Workflow definition '{id}' not found." });

            var existingStep = definition.StepDefinitions.FirstOrDefault(s => s.Key == stepKey);
            if (existingStep is null)
                return Results.NotFound(new { error = $"Step '{stepKey}' not found in definition '{id}'." });

            // Resolve retry policy from request
            RetryPolicy? stepRetryPolicy = request.EnableRetryPolicy
                ? new RetryPolicy(
                    request.RetryMaxAttempts ?? 3,
                    request.RetryInitialDelaySeconds ?? 10,
                    Enum.TryParse<BackoffStrategy>(request.RetryBackoffStrategy, true, out var bs)
                        ? bs : BackoffStrategy.Fixed,
                    request.RetryOnTimeout,
                    request.RetryMaxDelaySeconds)
                : null;

            StepDefinition updatedStep;
            try
            {
                updatedStep = existingStep.Type switch
                {
                    StepType.HttpRequest => StepDefinition.CreateHttpRequest(
                        existingStep.Id, existingStep.Key, existingStep.Order,
                        new HttpRequestStepConfiguration(
                            request.Url ?? existingStep.HttpRequestConfiguration!.Url,
                            request.Method ?? "POST",
                            ParseHeaders(request.Headers),
                            request.Body,
                            request.TimeoutSeconds,
                            existingStep.HttpRequestConfiguration!.ResponseClassification),
                        existingStep.RetryPolicyOverrideKey,
                        stepRetryPolicy),
                    StepType.SendWebhook => StepDefinition.CreateSendWebhook(
                        existingStep.Id, existingStep.Key, existingStep.Order,
                        new SendWebhookStepConfiguration(
                            request.Url ?? existingStep.SendWebhookConfiguration!.WebhookUrl,
                            request.Method ?? "POST",
                            ParseHeaders(request.Headers),
                            request.Body,
                            request.TimeoutSeconds),
                        existingStep.RetryPolicyOverrideKey,
                        stepRetryPolicy),
                    StepType.Transform => StepDefinition.CreateTransform(
                        existingStep.Id, existingStep.Key, existingStep.Order,
                        new TransformStepConfiguration(
                            ParseMappings(request.Mappings ?? "")),
                        existingStep.RetryPolicyOverrideKey,
                        stepRetryPolicy),
                    StepType.Conditional => StepDefinition.CreateConditional(
                        existingStep.Id, existingStep.Key, existingStep.Order,
                        new ConditionalStepConfiguration(
                            request.SourcePath ?? existingStep.ConditionalConfiguration!.SourcePath,
                            Enum.TryParse<ConditionalOperator>(request.Operator, true, out var op)
                                ? op : existingStep.ConditionalConfiguration!.Operator,
                            request.ExpectedValue,
                            Enum.TryParse<ConditionalFalseOutcome>(request.FalseOutcome, true, out var fo)
                                ? fo : ConditionalFalseOutcome.CompleteWorkflow),
                        existingStep.RetryPolicyOverrideKey,
                        stepRetryPolicy),
                    StepType.Delay => StepDefinition.CreateDelay(
                        existingStep.Id, existingStep.Key, existingStep.Order,
                        !string.IsNullOrWhiteSpace(request.TargetTimeExpression)
                            ? new DelayStepConfiguration(request.TargetTimeExpression)
                            : new DelayStepConfiguration(request.DelaySeconds ?? existingStep.DelayConfiguration!.DelaySeconds ?? 30),
                        existingStep.RetryPolicyOverrideKey,
                        stepRetryPolicy),
                    _ => throw new InvalidOperationException($"Unsupported step type '{existingStep.Type}'.")
                };
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var newSteps = definition.StepDefinitions
                .Select(s => s.Key == stepKey ? updatedStep : s);

            var now = DateTimeOffset.UtcNow;
            var updated = new StepTrail.Shared.Definitions.WorkflowDefinition(
                definition.Id, definition.Key, definition.Name, definition.Version,
                definition.Status, definition.TriggerDefinition, newSteps,
                definition.CreatedAtUtc, now, definition.Description,
                definition.SourceTemplateKey, definition.SourceTemplateVersion);

            try
            {
                await repository.UpdateAsync(updated, ct);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }

            return Results.Ok(new { id, stepKey, status = updated.Status.ToString() });
        });

        // ── DELETE steps/{key} ───────────────────────────────────────────────────────

        ops.MapDelete("/workflow-definitions/{id:guid}/steps/{stepKey}", async (
            Guid id,
            string stepKey,
            IWorkflowDefinitionRepository repository,
            CancellationToken ct) =>
        {
            var definition = await repository.GetByIdAsync(id, ct);
            if (definition is null)
                return Results.NotFound(new { error = $"Workflow definition '{id}' not found." });

            if (!definition.StepDefinitions.Any(s => s.Key == stepKey))
                return Results.NotFound(new { error = $"Step '{stepKey}' not found." });

            // Remove the step and re-order remaining steps sequentially.
            var remaining = definition.StepDefinitions
                .Where(s => s.Key != stepKey)
                .OrderBy(s => s.Order)
                .Select((s, i) => new StepDefinition(
                    s.Id, s.Key, i + 1, s.Type,
                    s.HttpRequestConfiguration, s.TransformConfiguration,
                    s.ConditionalConfiguration, s.DelayConfiguration,
                    s.SendWebhookConfiguration, s.RetryPolicyOverrideKey, s.RetryPolicy));

            var now = DateTimeOffset.UtcNow;
            var updated = new StepTrail.Shared.Definitions.WorkflowDefinition(
                definition.Id, definition.Key, definition.Name, definition.Version,
                definition.Status, definition.TriggerDefinition, remaining,
                definition.CreatedAtUtc, now, definition.Description,
                definition.SourceTemplateKey, definition.SourceTemplateVersion);

            try { await repository.UpdateAsync(updated, ct); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }

            return Results.Ok(new { id, removed = stepKey });
        });

        // ── POST move-up ─────────────────────────────────────────────────────────────

        ops.MapPost("/workflow-definitions/{id:guid}/steps/{stepKey}/move-up", async (
            Guid id,
            string stepKey,
            IWorkflowDefinitionRepository repository,
            CancellationToken ct) =>
        {
            var definition = await repository.GetByIdAsync(id, ct);
            if (definition is null)
                return Results.NotFound(new { error = $"Workflow definition '{id}' not found." });

            var ordered = definition.StepDefinitions.OrderBy(s => s.Order).ToList();
            var index = ordered.FindIndex(s => s.Key == stepKey);

            if (index < 0) return Results.NotFound(new { error = $"Step '{stepKey}' not found." });
            if (index == 0) return Results.Ok(new { id, stepKey, message = "Already first." });

            // Swap with previous
            (ordered[index], ordered[index - 1]) = (ordered[index - 1], ordered[index]);

            var reordered = ordered.Select((s, i) => new StepDefinition(
                s.Id, s.Key, i + 1, s.Type,
                s.HttpRequestConfiguration, s.TransformConfiguration,
                s.ConditionalConfiguration, s.DelayConfiguration,
                s.SendWebhookConfiguration, s.RetryPolicyOverrideKey, s.RetryPolicy));

            var now = DateTimeOffset.UtcNow;
            var updated = new StepTrail.Shared.Definitions.WorkflowDefinition(
                definition.Id, definition.Key, definition.Name, definition.Version,
                definition.Status, definition.TriggerDefinition, reordered,
                definition.CreatedAtUtc, now, definition.Description,
                definition.SourceTemplateKey, definition.SourceTemplateVersion);

            try { await repository.UpdateAsync(updated, ct); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }

            return Results.Ok(new { id, stepKey, newOrder = index });
        });

        // ── POST move-down ───────────────────────────────────────────────────────────

        ops.MapPost("/workflow-definitions/{id:guid}/steps/{stepKey}/move-down", async (
            Guid id,
            string stepKey,
            IWorkflowDefinitionRepository repository,
            CancellationToken ct) =>
        {
            var definition = await repository.GetByIdAsync(id, ct);
            if (definition is null)
                return Results.NotFound(new { error = $"Workflow definition '{id}' not found." });

            var ordered = definition.StepDefinitions.OrderBy(s => s.Order).ToList();
            var index = ordered.FindIndex(s => s.Key == stepKey);

            if (index < 0) return Results.NotFound(new { error = $"Step '{stepKey}' not found." });
            if (index >= ordered.Count - 1) return Results.Ok(new { id, stepKey, message = "Already last." });

            // Swap with next
            (ordered[index], ordered[index + 1]) = (ordered[index + 1], ordered[index]);

            var reordered = ordered.Select((s, i) => new StepDefinition(
                s.Id, s.Key, i + 1, s.Type,
                s.HttpRequestConfiguration, s.TransformConfiguration,
                s.ConditionalConfiguration, s.DelayConfiguration,
                s.SendWebhookConfiguration, s.RetryPolicyOverrideKey, s.RetryPolicy));

            var now = DateTimeOffset.UtcNow;
            var updated = new StepTrail.Shared.Definitions.WorkflowDefinition(
                definition.Id, definition.Key, definition.Name, definition.Version,
                definition.Status, definition.TriggerDefinition, reordered,
                definition.CreatedAtUtc, now, definition.Description,
                definition.SourceTemplateKey, definition.SourceTemplateVersion);

            try { await repository.UpdateAsync(updated, ct); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }

            return Results.Ok(new { id, stepKey, newOrder = index + 2 });
        });

        // ── POST activate ────────────────────────────────────────────────────────────

        ops.MapPost("/workflow-definitions/{id:guid}/activate", async (
            Guid id,
            IWorkflowDefinitionRepository repository,
            TelemetryService telemetry,
            CancellationToken ct) =>
        {
            var definition = await repository.GetByIdAsync(id, ct);
            if (definition is null)
                return Results.NotFound(new { error = $"Workflow definition '{id}' not found." });

            if (definition.Status == WorkflowDefinitionStatus.Active)
                return Results.Ok(new { id, status = "Active", message = "Already active." });

            if (definition.Status is not (WorkflowDefinitionStatus.Draft or WorkflowDefinitionStatus.Inactive))
                return Results.Conflict(new { error = $"Cannot activate a definition in '{definition.Status}' status." });

            var now = DateTimeOffset.UtcNow;
            var activated = new StepTrail.Shared.Definitions.WorkflowDefinition(
                definition.Id, definition.Key, definition.Name, definition.Version,
                WorkflowDefinitionStatus.Active, definition.TriggerDefinition, definition.StepDefinitions,
                definition.CreatedAtUtc, now, definition.Description,
                definition.SourceTemplateKey, definition.SourceTemplateVersion);

            try
            {
                await repository.UpdateAsync(activated, ct);
            }
            catch (WorkflowDefinitionValidationException ex)
            {
                var errors = ex.ValidationResult.Errors.Select(e => e.Message).ToList();
                await telemetry.RecordAsync(TelemetryEvents.ActivationFailed, TelemetryEvents.Categories.Error, ct,
                    workflowKey: definition.Key, workflowDefinitionId: id,
                    metadata: new { errors });
                return Results.BadRequest(new { error = "Activation validation failed.", errors });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }

            await telemetry.RecordAsync(TelemetryEvents.WorkflowActivated, TelemetryEvents.Categories.Authoring, ct,
                workflowKey: definition.Key, workflowDefinitionId: id, status: WorkflowDefinitionStatus.Active.ToString(),
                triggerType: definition.TriggerDefinition.Type.ToString(),
                metadata: new { stepCount = definition.StepDefinitions.Count });

            return Results.Ok(new { id, status = "Active" });
        });

        // ── POST deactivate ──────────────────────────────────────────────────────────

        ops.MapPost("/workflow-definitions/{id:guid}/deactivate", async (
            Guid id,
            StepTrailDbContext db,
            TelemetryService telemetry,
            CancellationToken ct) =>
        {
            var record = await db.ExecutableWorkflowDefinitions.FindAsync([id], ct);
            if (record is null)
                return Results.NotFound(new { error = $"Workflow definition '{id}' not found." });

            if (record.Status == WorkflowDefinitionStatus.Inactive)
                return Results.Ok(new { id, status = "Inactive", message = "Already inactive." });

            if (record.Status != WorkflowDefinitionStatus.Active)
                return Results.Conflict(new { error = $"Cannot deactivate a definition in '{record.Status}' status." });

            record.Status = WorkflowDefinitionStatus.Inactive;
            record.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            await telemetry.RecordAsync(TelemetryEvents.WorkflowDeactivated, TelemetryEvents.Categories.Authoring, ct,
                workflowKey: record.Key, workflowDefinitionId: id, status: record.Status.ToString());

            return Results.Ok(new { id, status = "Inactive" });
        });

        return ops;
    }

    // ── Helper methods ───────────────────────────────────────────────────────────

    private static bool IsValidWorkflowKey(string key) =>
        Regex.IsMatch(key, @"^[a-z0-9][a-z0-9\-]*[a-z0-9]$");

    private static bool IsSupportedStepType(string stepType) =>
        stepType.Equals("HttpRequest", StringComparison.OrdinalIgnoreCase)
        || stepType.Equals("SendWebhook", StringComparison.OrdinalIgnoreCase)
        || stepType.Equals("Transform", StringComparison.OrdinalIgnoreCase)
        || stepType.Equals("Conditional", StringComparison.OrdinalIgnoreCase)
        || stepType.Equals("Delay", StringComparison.OrdinalIgnoreCase);

    private static TriggerDefinition CreateDefaultTrigger(TriggerType triggerType, string workflowKey) =>
        triggerType switch
        {
            TriggerType.Webhook => TriggerDefinition.CreateWebhook(Guid.NewGuid(), new WebhookTriggerConfiguration(workflowKey)),
            TriggerType.Manual => TriggerDefinition.CreateManual(Guid.NewGuid(), new ManualTriggerConfiguration("ops-console")),
            TriggerType.Schedule => TriggerDefinition.CreateSchedule(Guid.NewGuid(), new ScheduleTriggerConfiguration(300)),
            _ => throw new InvalidOperationException($"Unsupported trigger type '{triggerType}'.")
        };

    private static StepDefinition CreateDefaultStepDefinition(string stepKey, int order, string stepType)
    {
        if (Enum.TryParse<StepType>(stepType, ignoreCase: true, out var parsed))
        {
            return parsed switch
            {
                StepType.HttpRequest => StepDefinition.CreateHttpRequest(Guid.NewGuid(), stepKey, order,
                    new HttpRequestStepConfiguration("https://api.example.com/endpoint")),
                StepType.SendWebhook => StepDefinition.CreateSendWebhook(Guid.NewGuid(), stepKey, order,
                    new SendWebhookStepConfiguration("https://hooks.example.com/outbound")),
                StepType.Transform => StepDefinition.CreateTransform(Guid.NewGuid(), stepKey, order,
                    new TransformStepConfiguration([new TransformValueMapping("output", "{{input}}")])),
                StepType.Conditional => StepDefinition.CreateConditional(Guid.NewGuid(), stepKey, order,
                    new ConditionalStepConfiguration("{{input.status}}", ConditionalOperator.Equals, "ready")),
                StepType.Delay => StepDefinition.CreateDelay(Guid.NewGuid(), stepKey, order,
                    new DelayStepConfiguration(30)),
                _ => throw new ArgumentException($"Unsupported step type '{stepType}'.", nameof(stepType))
            };
        }

        throw new ArgumentException(
            $"Unsupported step type '{stepType}'. Supported: HttpRequest, SendWebhook, Transform, Conditional, Delay.",
            nameof(stepType));
    }

    private static Dictionary<string, string> ParseHeaders(string? headersText)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(headersText)) return result;

        foreach (var line in headersText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            var colonIndex = trimmed.IndexOf(':');
            if (colonIndex <= 0)
            {
                // No colon found — treat the whole line as a header name with empty value.
                // This prevents silent data loss when users forget the colon.
                result[trimmed] = string.Empty;
                continue;
            }

            var key = trimmed[..colonIndex].Trim();
            var value = trimmed[(colonIndex + 1)..].Trim();
            if (!string.IsNullOrEmpty(key))
                result[key] = value;
        }
        return result;
    }

    private static IEnumerable<TransformValueMapping> ParseMappings(string mappingsText)
    {
        if (string.IsNullOrWhiteSpace(mappingsText))
            return [new TransformValueMapping("output", "{{input}}")];

        var mappings = new List<TransformValueMapping>();
        foreach (var line in mappingsText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIndex = line.IndexOf('=');
            if (eqIndex <= 0) continue;
            var target = line[..eqIndex].Trim();
            var source = line[(eqIndex + 1)..].Trim();
            if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(source))
                continue;

            // Recognize operation syntax from the display format and reconstruct operations
            if (source.StartsWith("default(", StringComparison.Ordinal) && source.EndsWith(")"))
            {
                var inner = source[8..^1]; // strip "default(" and ")"
                var commaIdx = inner.IndexOf(',');
                if (commaIdx > 0)
                {
                    var srcPath = inner[..commaIdx].Trim();
                    var defVal = inner[(commaIdx + 1)..].Trim().Trim('"');
                    mappings.Add(new TransformValueMapping(target,
                        TransformValueOperation.CreateDefaultValue(srcPath, defVal)));
                    continue;
                }
            }
            else if (source.StartsWith("concat(", StringComparison.Ordinal) && source.EndsWith(")"))
            {
                var inner = source[7..^1]; // strip "concat(" and ")"
                var parts = inner.Split(',', StringSplitOptions.TrimEntries)
                    .Where(p => !string.IsNullOrEmpty(p)).ToList();
                if (parts.Count > 0)
                {
                    mappings.Add(new TransformValueMapping(target,
                        TransformValueOperation.CreateConcatenate(parts)));
                    continue;
                }
            }
            else if (source.StartsWith("format(", StringComparison.Ordinal) && source.EndsWith(")"))
            {
                var inner = source[7..^1]; // strip "format(" and ")"
                var firstComma = inner.IndexOf(',');
                if (firstComma > 0)
                {
                    var template = inner[..firstComma].Trim().Trim('"');
                    var args = inner[(firstComma + 1)..].Split(',', StringSplitOptions.TrimEntries)
                        .Where(a => !string.IsNullOrEmpty(a)).ToList();
                    if (args.Count > 0)
                    {
                        mappings.Add(new TransformValueMapping(target,
                            TransformValueOperation.CreateFormatString(template, args)));
                        continue;
                    }
                }
            }

            // Simple source path mapping
            mappings.Add(new TransformValueMapping(target, source));
        }

        return mappings.Count > 0 ? mappings : [new TransformValueMapping("output", "{{input}}")];
    }
}
