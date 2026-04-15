using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StepTrail.Shared.Entities;
using StepTrail.Shared.Definitions.Persistence;
using StepTrail.Shared.Runtime.Scheduling;

namespace StepTrail.Shared.Definitions;

public sealed class WorkflowDefinitionRepository : IWorkflowDefinitionRepository
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly StepTrailDbContext _db;
    private readonly IWorkflowDefinitionActivationGuard _workflowDefinitionActivationGuard;

    public WorkflowDefinitionRepository(
        StepTrailDbContext db,
        IWorkflowDefinitionActivationGuard? workflowDefinitionActivationGuard = null)
    {
        _db = db;
        _workflowDefinitionActivationGuard = workflowDefinitionActivationGuard
            ?? new WorkflowDefinitionActivationGuard(new WorkflowDefinitionValidator());
    }

    public async Task<WorkflowDefinition> CreateDraftAsync(
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.Status != WorkflowDefinitionStatus.Draft)
            throw new InvalidOperationException("CreateDraftAsync requires the workflow definition status to be Draft.");

        _db.ExecutableWorkflowDefinitions.Add(MapToRecord(definition));
        await _db.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(definition.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow definition '{definition.Id}' was not found after creation.");
    }

    public async Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await LoadDefinitionRecordQuery()
            .SingleOrDefaultAsync(definition => definition.Id == id, cancellationToken);

        return record is null ? null : MapToDomain(record);
    }

    public async Task<WorkflowDefinition?> GetByKeyAndVersionAsync(
        string key,
        int version,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Workflow definition key must not be empty.", nameof(key));
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version), "Workflow definition version must be 1 or greater.");

        var record = await LoadDefinitionRecordQuery()
            .SingleOrDefaultAsync(
                definition => definition.Key == key.Trim() && definition.Version == version,
                cancellationToken);

        return record is null ? null : MapToDomain(record);
    }

    public async Task<WorkflowDefinition?> GetActiveByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Workflow definition key must not be empty.", nameof(key));

        var record = await LoadDefinitionRecordQuery()
            .SingleOrDefaultAsync(
                definition => definition.Key == key.Trim() && definition.Status == WorkflowDefinitionStatus.Active,
                cancellationToken);

        return record is null ? null : MapToDomain(record);
    }

    public async Task<WorkflowDefinition?> GetActiveWebhookByRouteKeyAsync(
        string routeKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(routeKey))
            throw new ArgumentException("Webhook route key must not be empty.", nameof(routeKey));

        var normalizedRouteKey = routeKey.Trim();

        var record = await LoadDefinitionRecordQuery()
            .SingleOrDefaultAsync(
                definition =>
                    definition.Status == WorkflowDefinitionStatus.Active &&
                    definition.WebhookRouteKey == normalizedRouteKey,
                cancellationToken);

        return record is null ? null : MapToDomain(record);
    }

    public async Task<WorkflowDefinition> UpdateAsync(
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var record = await _db.ExecutableWorkflowDefinitions
            .Include(workflowDefinition => workflowDefinition.TriggerDefinition)
            .Include(workflowDefinition => workflowDefinition.StepDefinitions)
            .SingleOrDefaultAsync(workflowDefinition => workflowDefinition.Id == definition.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow definition '{definition.Id}' was not found.");

        if (record.Status != WorkflowDefinitionStatus.Draft)
        {
            throw new InvalidOperationException(
                $"Workflow definition '{definition.Id}' is in '{record.Status}' status and can no longer be updated in place. " +
                "Create a new version instead.");
        }

        if (!string.Equals(record.Key, definition.Key, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Workflow definition '{definition.Id}' cannot change its key from '{record.Key}' to '{definition.Key}'.");
        }

        if (record.Version != definition.Version)
        {
            throw new InvalidOperationException(
                $"Workflow definition '{definition.Id}' cannot change its version from '{record.Version}' to '{definition.Version}'.");
        }

        if (definition.Status == WorkflowDefinitionStatus.Active)
            _workflowDefinitionActivationGuard.EnsureCanActivate(definition);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        if (definition.Status == WorkflowDefinitionStatus.Active)
            await DeactivateOtherActiveVersionsAsync(definition.Key, definition.Id, definition.UpdatedAtUtc, cancellationToken);

        record.Key = definition.Key;
        record.WebhookRouteKey = GetWebhookRouteKey(definition.TriggerDefinition);
        record.Name = definition.Name;
        record.Version = definition.Version;
        record.Status = definition.Status;
        record.Description = definition.Description;
        record.CreatedAtUtc = definition.CreatedAtUtc;
        record.UpdatedAtUtc = definition.UpdatedAtUtc;

        if (record.TriggerDefinition is not null)
            _db.ExecutableTriggerDefinitions.Remove(record.TriggerDefinition);

        if (record.StepDefinitions.Count > 0)
        {
            _db.ExecutableStepDefinitions.RemoveRange(record.StepDefinitions);
            record.StepDefinitions.Clear();
        }

        await _db.SaveChangesAsync(cancellationToken);

        record.TriggerDefinition = MapToRecord(record.Id, definition.TriggerDefinition);
        record.StepDefinitions = definition.StepDefinitions
            .Select(stepDefinition => MapToRecord(record.Id, stepDefinition))
            .ToList();

        if (definition.Status == WorkflowDefinitionStatus.Active)
            await SyncExecutableRecurringScheduleAsync(definition, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetByIdAsync(definition.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow definition '{definition.Id}' was not found after update.");
    }

    public async Task<WorkflowDefinition> SaveNewVersionAsync(
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.Status == WorkflowDefinitionStatus.Active)
            _workflowDefinitionActivationGuard.EnsureCanActivate(definition);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        if (definition.Status == WorkflowDefinitionStatus.Active)
            await DeactivateOtherActiveVersionsAsync(definition.Key, definition.Id, definition.UpdatedAtUtc, cancellationToken);

        _db.ExecutableWorkflowDefinitions.Add(MapToRecord(definition));

        if (definition.Status == WorkflowDefinitionStatus.Active)
            await SyncExecutableRecurringScheduleAsync(definition, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetByIdAsync(definition.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow definition '{definition.Id}' was not found after saving a new version.");
    }

    private IQueryable<ExecutableWorkflowDefinitionRecord> LoadDefinitionRecordQuery() =>
        _db.ExecutableWorkflowDefinitions
            .AsNoTracking()
            .Include(workflowDefinition => workflowDefinition.TriggerDefinition)
            .Include(workflowDefinition => workflowDefinition.StepDefinitions);

    private Task<int> DeactivateOtherActiveVersionsAsync(
        string key,
        Guid currentDefinitionId,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken) =>
        _db.ExecutableWorkflowDefinitions
            .Where(definition =>
                definition.Key == key &&
                definition.Id != currentDefinitionId &&
                definition.Status == WorkflowDefinitionStatus.Active)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(definition => definition.Status, WorkflowDefinitionStatus.Inactive)
                    .SetProperty(definition => definition.UpdatedAtUtc, updatedAtUtc),
                cancellationToken);

    private async Task SyncExecutableRecurringScheduleAsync(
        WorkflowDefinition definition,
        CancellationToken cancellationToken)
    {
        var existingSchedule = await _db.RecurringWorkflowSchedules
            .SingleOrDefaultAsync(
                schedule => schedule.ExecutableWorkflowKey == definition.Key,
                cancellationToken);
        var now = definition.UpdatedAtUtc;

        if (definition.TriggerDefinition.Type != TriggerType.Schedule)
        {
            if (existingSchedule is not null)
            {
                existingSchedule.IsEnabled = false;
                existingSchedule.UpdatedAt = now;
            }

            return;
        }

        var scheduleConfiguration = definition.TriggerDefinition.ScheduleConfiguration
            ?? throw new InvalidOperationException(
                $"Workflow definition '{definition.Key}' is missing schedule trigger configuration.");
        var defaultTenantExists = await _db.Tenants
            .AnyAsync(tenant => tenant.Id == StepTrailRuntimeDefaults.DefaultTenantId, cancellationToken);

        if (!defaultTenantExists)
        {
            throw new InvalidOperationException(
                $"Default tenant '{StepTrailRuntimeDefaults.DefaultTenantId}' must exist before activating scheduled workflow definitions.");
        }

        if (existingSchedule is null)
        {
            _db.RecurringWorkflowSchedules.Add(new RecurringWorkflowSchedule
            {
                Id = Guid.NewGuid(),
                ExecutableWorkflowKey = definition.Key,
                TenantId = StepTrailRuntimeDefaults.DefaultTenantId,
                IntervalSeconds = scheduleConfiguration.IntervalSeconds,
                CronExpression = scheduleConfiguration.CronExpression,
                IsEnabled = true,
                NextRunAt = ScheduleTriggerTimingCalculator.GetInitialNextRunAt(scheduleConfiguration, now),
                CreatedAt = now,
                UpdatedAt = now
            });

            return;
        }

        var wasEnabled = existingSchedule.IsEnabled;
        var timingChanged =
            existingSchedule.IntervalSeconds != scheduleConfiguration.IntervalSeconds
            || !string.Equals(existingSchedule.CronExpression, scheduleConfiguration.CronExpression, StringComparison.Ordinal);

        existingSchedule.WorkflowDefinitionId = null;
        existingSchedule.ExecutableWorkflowKey = definition.Key;
        existingSchedule.TenantId = StepTrailRuntimeDefaults.DefaultTenantId;
        existingSchedule.IntervalSeconds = scheduleConfiguration.IntervalSeconds;
        existingSchedule.CronExpression = scheduleConfiguration.CronExpression;
        existingSchedule.IsEnabled = true;
        existingSchedule.UpdatedAt = now;

        if (!wasEnabled || timingChanged || existingSchedule.LastRunAt is null)
        {
            existingSchedule.NextRunAt = ScheduleTriggerTimingCalculator.GetResynchronizedNextRunAt(
                scheduleConfiguration,
                existingSchedule.LastRunAt,
                now);
        }
    }

    private static ExecutableWorkflowDefinitionRecord MapToRecord(WorkflowDefinition definition) =>
        new()
        {
            Id = definition.Id,
            Key = definition.Key,
            WebhookRouteKey = GetWebhookRouteKey(definition.TriggerDefinition),
            Name = definition.Name,
            Version = definition.Version,
            Status = definition.Status,
            Description = definition.Description,
            CreatedAtUtc = definition.CreatedAtUtc,
            UpdatedAtUtc = definition.UpdatedAtUtc,
            TriggerDefinition = MapToRecord(definition.Id, definition.TriggerDefinition),
            StepDefinitions = definition.StepDefinitions
                .Select(stepDefinition => MapToRecord(definition.Id, stepDefinition))
                .ToList()
        };

    private static ExecutableTriggerDefinitionRecord MapToRecord(Guid workflowDefinitionId, TriggerDefinition triggerDefinition) =>
        new()
        {
            Id = triggerDefinition.Id,
            WorkflowDefinitionId = workflowDefinitionId,
            Type = triggerDefinition.Type,
            Configuration = SerializeTriggerConfiguration(triggerDefinition)
        };

    private static ExecutableStepDefinitionRecord MapToRecord(Guid workflowDefinitionId, StepDefinition stepDefinition) =>
        new()
        {
            Id = stepDefinition.Id,
            WorkflowDefinitionId = workflowDefinitionId,
            Key = stepDefinition.Key,
            Order = stepDefinition.Order,
            Type = stepDefinition.Type,
            RetryPolicyOverrideKey = stepDefinition.RetryPolicyOverrideKey,
            Configuration = SerializeStepConfiguration(stepDefinition)
        };

    private static string? GetWebhookRouteKey(TriggerDefinition triggerDefinition) =>
        triggerDefinition.Type == TriggerType.Webhook
            ? triggerDefinition.WebhookConfiguration?.RouteKey
            : null;

    private static WorkflowDefinition MapToDomain(ExecutableWorkflowDefinitionRecord record) =>
        new(
            record.Id,
            record.Key,
            record.Name,
            record.Version,
            record.Status,
            MapToDomain(record.TriggerDefinition),
            record.StepDefinitions
                .OrderBy(stepDefinition => stepDefinition.Order)
                .Select(MapToDomain)
                .ToList(),
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Description);

    private static TriggerDefinition MapToDomain(ExecutableTriggerDefinitionRecord record) =>
        record.Type switch
        {
            TriggerType.Webhook => TriggerDefinition.CreateWebhook(
                record.Id,
                DeserializeWebhookTriggerConfiguration(record.Configuration)),
            TriggerType.Manual => TriggerDefinition.CreateManual(
                record.Id,
                DeserializeManualTriggerConfiguration(record.Configuration)),
            TriggerType.Api => TriggerDefinition.CreateApi(
                record.Id,
                DeserializeApiTriggerConfiguration(record.Configuration)),
            TriggerType.Schedule => TriggerDefinition.CreateSchedule(
                record.Id,
                DeserializeScheduleTriggerConfiguration(record.Configuration)),
            _ => throw new InvalidOperationException($"Unsupported trigger type '{record.Type}'.")
        };

    private static StepDefinition MapToDomain(ExecutableStepDefinitionRecord record) =>
        record.Type switch
        {
            StepType.HttpRequest => StepDefinition.CreateHttpRequest(
                record.Id,
                record.Key,
                record.Order,
                DeserializeHttpRequestStepConfiguration(record.Configuration),
                record.RetryPolicyOverrideKey),
            StepType.Transform => StepDefinition.CreateTransform(
                record.Id,
                record.Key,
                record.Order,
                DeserializeTransformStepConfiguration(record.Configuration),
                record.RetryPolicyOverrideKey),
            StepType.Conditional => StepDefinition.CreateConditional(
                record.Id,
                record.Key,
                record.Order,
                DeserializeConditionalStepConfiguration(record.Configuration),
                record.RetryPolicyOverrideKey),
            StepType.Delay => StepDefinition.CreateDelay(
                record.Id,
                record.Key,
                record.Order,
                DeserializeDelayStepConfiguration(record.Configuration),
                record.RetryPolicyOverrideKey),
            StepType.SendWebhook => StepDefinition.CreateSendWebhook(
                record.Id,
                record.Key,
                record.Order,
                DeserializeSendWebhookStepConfiguration(record.Configuration),
                record.RetryPolicyOverrideKey),
            _ => throw new InvalidOperationException($"Unsupported step type '{record.Type}'.")
        };

    private static string SerializeTriggerConfiguration(TriggerDefinition triggerDefinition) =>
        triggerDefinition.Type switch
        {
            TriggerType.Webhook => JsonSerializer.Serialize(triggerDefinition.WebhookConfiguration!, JsonSerializerOptions),
            TriggerType.Manual => JsonSerializer.Serialize(triggerDefinition.ManualConfiguration!, JsonSerializerOptions),
            TriggerType.Api => JsonSerializer.Serialize(triggerDefinition.ApiConfiguration!, JsonSerializerOptions),
            TriggerType.Schedule => JsonSerializer.Serialize(triggerDefinition.ScheduleConfiguration!, JsonSerializerOptions),
            _ => throw new InvalidOperationException($"Unsupported trigger type '{triggerDefinition.Type}'.")
        };

    private static string SerializeStepConfiguration(StepDefinition stepDefinition) =>
        stepDefinition.Type switch
        {
            StepType.HttpRequest => JsonSerializer.Serialize(stepDefinition.HttpRequestConfiguration!, JsonSerializerOptions),
            StepType.Transform => JsonSerializer.Serialize(stepDefinition.TransformConfiguration!, JsonSerializerOptions),
            StepType.Conditional => JsonSerializer.Serialize(stepDefinition.ConditionalConfiguration!, JsonSerializerOptions),
            StepType.Delay => JsonSerializer.Serialize(stepDefinition.DelayConfiguration!, JsonSerializerOptions),
            StepType.SendWebhook => JsonSerializer.Serialize(stepDefinition.SendWebhookConfiguration!, JsonSerializerOptions),
            _ => throw new InvalidOperationException($"Unsupported step type '{stepDefinition.Type}'.")
        };

    private static WebhookTriggerConfiguration DeserializeWebhookTriggerConfiguration(string json)
    {
        var dto = DeserializeDto<WebhookTriggerConfigurationDto>(json, "webhook trigger configuration");
        var signatureValidation = dto.SignatureValidation is null
            ? null
            : new WebhookSignatureValidationConfiguration(
                dto.SignatureValidation.HeaderName,
                dto.SignatureValidation.SecretName,
                dto.SignatureValidation.Algorithm,
                dto.SignatureValidation.SignaturePrefix);
        var idempotencyKeyExtraction = dto.IdempotencyKeyExtraction is null
            ? null
            : new WebhookIdempotencyKeyExtractionConfiguration(dto.IdempotencyKeyExtraction.SourcePath);

        return new WebhookTriggerConfiguration(
            dto.RouteKey,
            dto.HttpMethod ?? "POST",
            signatureValidation,
            dto.InputMappings?.Select(mapping => new WebhookInputMapping(mapping.TargetPath, mapping.SourcePath)),
            idempotencyKeyExtraction);
    }

    private static ManualTriggerConfiguration DeserializeManualTriggerConfiguration(string json)
    {
        var dto = DeserializeDto<ManualTriggerConfigurationDto>(json, "manual trigger configuration");
        return new ManualTriggerConfiguration(dto.EntryPointKey);
    }

    private static ApiTriggerConfiguration DeserializeApiTriggerConfiguration(string json)
    {
        var dto = DeserializeDto<ApiTriggerConfigurationDto>(json, "api trigger configuration");
        return new ApiTriggerConfiguration(dto.OperationKey);
    }

    private static ScheduleTriggerConfiguration DeserializeScheduleTriggerConfiguration(string json)
    {
        var dto = DeserializeDto<ScheduleTriggerConfigurationDto>(json, "schedule trigger configuration");
        if (!string.IsNullOrWhiteSpace(dto.CronExpression))
            return new ScheduleTriggerConfiguration(dto.CronExpression);
        if (dto.IntervalSeconds.HasValue)
            return new ScheduleTriggerConfiguration(dto.IntervalSeconds.Value);

        throw new InvalidOperationException("Schedule trigger configuration must define intervalSeconds or cronExpression.");
    }

    private static HttpRequestStepConfiguration DeserializeHttpRequestStepConfiguration(string json)
    {
        var dto = DeserializeDto<HttpRequestStepConfigurationDto>(json, "http request step configuration");
        return new HttpRequestStepConfiguration(dto.Url, dto.Method ?? "POST", dto.Headers, dto.Body);
    }

    private static TransformStepConfiguration DeserializeTransformStepConfiguration(string json)
    {
        var dto = DeserializeDto<TransformStepConfigurationDto>(json, "transform step configuration");
        return new TransformStepConfiguration(
            dto.Mappings.Select(mapping => new TransformValueMapping(mapping.TargetPath, mapping.SourcePath)));
    }

    private static ConditionalStepConfiguration DeserializeConditionalStepConfiguration(string json)
    {
        var dto = DeserializeDto<ConditionalStepConfigurationDto>(json, "conditional step configuration");
        return new ConditionalStepConfiguration(dto.ConditionExpression);
    }

    private static DelayStepConfiguration DeserializeDelayStepConfiguration(string json)
    {
        var dto = DeserializeDto<DelayStepConfigurationDto>(json, "delay step configuration");
        return new DelayStepConfiguration(dto.DelaySeconds);
    }

    private static SendWebhookStepConfiguration DeserializeSendWebhookStepConfiguration(string json)
    {
        var dto = DeserializeDto<SendWebhookStepConfigurationDto>(json, "send webhook step configuration");
        return new SendWebhookStepConfiguration(dto.WebhookUrl, dto.Method ?? "POST", dto.Headers, dto.Body);
    }

    private static TDto DeserializeDto<TDto>(string json, string configurationDescription)
    {
        var dto = JsonSerializer.Deserialize<TDto>(json, JsonSerializerOptions);

        return dto
            ?? throw new InvalidOperationException($"Failed to deserialize {configurationDescription}.");
    }

    private sealed class WebhookTriggerConfigurationDto
    {
        public string RouteKey { get; set; } = string.Empty;
        public string? HttpMethod { get; set; }
        public WebhookSignatureValidationConfigurationDto? SignatureValidation { get; set; }
        public List<WebhookInputMappingDto>? InputMappings { get; set; }
        public WebhookIdempotencyKeyExtractionConfigurationDto? IdempotencyKeyExtraction { get; set; }
    }

    private sealed class WebhookSignatureValidationConfigurationDto
    {
        public string HeaderName { get; set; } = string.Empty;
        public string SecretName { get; set; } = string.Empty;
        public WebhookSignatureAlgorithm Algorithm { get; set; }
        public string? SignaturePrefix { get; set; }
    }

    private sealed class WebhookInputMappingDto
    {
        public string TargetPath { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
    }

    private sealed class WebhookIdempotencyKeyExtractionConfigurationDto
    {
        public string SourcePath { get; set; } = string.Empty;
    }

    private sealed class ManualTriggerConfigurationDto
    {
        public string EntryPointKey { get; set; } = string.Empty;
    }

    private sealed class ApiTriggerConfigurationDto
    {
        public string OperationKey { get; set; } = string.Empty;
    }

    private sealed class ScheduleTriggerConfigurationDto
    {
        public int? IntervalSeconds { get; set; }
        public string? CronExpression { get; set; }
    }

    private sealed class HttpRequestStepConfigurationDto
    {
        public string Url { get; set; } = string.Empty;
        public string? Method { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
        public string? Body { get; set; }
    }

    private sealed class TransformStepConfigurationDto
    {
        public List<TransformValueMappingDto> Mappings { get; set; } = [];
    }

    private sealed class TransformValueMappingDto
    {
        public string TargetPath { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
    }

    private sealed class ConditionalStepConfigurationDto
    {
        public string ConditionExpression { get; set; } = string.Empty;
    }

    private sealed class DelayStepConfigurationDto
    {
        public int DelaySeconds { get; set; }
    }

    private sealed class SendWebhookStepConfigurationDto
    {
        public string WebhookUrl { get; set; } = string.Empty;
        public string? Method { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
        public string? Body { get; set; }
    }
}
