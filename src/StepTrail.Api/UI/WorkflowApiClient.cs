using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using StepTrail.Api.Models;

namespace StepTrail.Api.UI;

/// <summary>
/// Typed HTTP client used by Razor Pages to interact with the existing REST API.
/// Keeps page models free of raw HttpClient usage and provides explicit, focused methods.
/// </summary>
public sealed class WorkflowApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<WorkflowApiClient> _logger;

    public WorkflowApiClient(HttpClient http, ILogger<WorkflowApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<PagedResult<WorkflowInstanceSummary>> ListInstancesAsync(
        string? status = null,
        string? workflowKey = null,
        string? triggerType = null,
        DateTimeOffset? createdFrom = null,
        DateTimeOffset? createdTo = null,
        bool includeArchived = false,
        int page = 1,
        int pageSize = 100,
        CancellationToken ct = default)
    {
        var url = $"/workflow-instances?page={page}&pageSize={pageSize}&includeArchived={includeArchived}";
        if (!string.IsNullOrWhiteSpace(status))
            url += $"&status={Uri.EscapeDataString(status)}";
        if (!string.IsNullOrWhiteSpace(workflowKey))
            url += $"&workflowKey={Uri.EscapeDataString(workflowKey)}";
        if (!string.IsNullOrWhiteSpace(triggerType))
            url += $"&triggerType={Uri.EscapeDataString(triggerType)}";
        if (createdFrom.HasValue)
            url += $"&createdFrom={Uri.EscapeDataString(createdFrom.Value.ToString("O"))}";
        if (createdTo.HasValue)
            url += $"&createdTo={Uri.EscapeDataString(createdTo.Value.ToString("O"))}";

        var result = await _http.GetFromJsonAsync<PagedResult<WorkflowInstanceSummary>>(url, ct);
        return result ?? new PagedResult<WorkflowInstanceSummary>();
    }

    public async Task<IReadOnlyList<WorkflowDescriptorSummary>> ListWorkflowsAsync(
        CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<List<WorkflowDescriptorSummary>>("/workflows", ct);
        return result ?? [];
    }

    public async Task<IReadOnlyList<WorkflowDefinitionSummary>> ListDefinitionsAsync(
        CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<List<WorkflowDefinitionSummary>>("/workflow-definitions", ct);
        return result ?? [];
    }

    public async Task<WorkflowInstanceDetail?> GetInstanceAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<WorkflowInstanceDetail>(
                $"/workflow-instances/{id}", ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<WorkflowTrail?> GetTrailAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<WorkflowTrail>(
                $"/workflow-instances/{id}/trail", ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<WorkflowDefinitionDetail?> GetDefinitionAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<WorkflowDefinitionDetail>(
                $"/workflow-definitions/{id}", ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<ApiActionResult> ChangeTriggerTypeAsync(
        Guid definitionId,
        string triggerType,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PutAsJsonAsync(
                $"/workflow-definitions/{definitionId}/trigger-type",
                new { triggerType }, ct);

            if (response.IsSuccessStatusCode)
                return ApiActionResult.Ok();

            var body = await response.Content.ReadAsStringAsync(ct);
            return ApiActionResult.Fail(ExtractErrorMessage(body) ?? $"API returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing trigger type for definition {Id}", definitionId);
            return ApiActionResult.Fail("An unexpected error occurred.");
        }
    }

    public async Task<ApiActionResult> UpdateTriggerAsync(
        Guid definitionId,
        UpdateTriggerRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PutAsJsonAsync(
                $"/workflow-definitions/{definitionId}/trigger", request, ct);

            if (response.IsSuccessStatusCode)
                return ApiActionResult.Ok();

            var body = await response.Content.ReadAsStringAsync(ct);
            return ApiActionResult.Fail(ExtractErrorMessage(body) ?? $"API returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating trigger for definition {Id}", definitionId);
            return ApiActionResult.Fail("An unexpected error occurred.");
        }
    }

    public async Task<ApiActionResult> RemoveStepAsync(
        Guid definitionId, string stepKey, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.DeleteAsync(
                $"/workflow-definitions/{definitionId}/steps/{Uri.EscapeDataString(stepKey)}", ct);
            if (response.IsSuccessStatusCode) return ApiActionResult.Ok();
            var body = await response.Content.ReadAsStringAsync(ct);
            return ApiActionResult.Fail(ExtractErrorMessage(body) ?? $"API returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing step {StepKey} from {Id}", stepKey, definitionId);
            return ApiActionResult.Fail("An unexpected error occurred.");
        }
    }

    public async Task<ApiActionResult> MoveStepUpAsync(
        Guid definitionId, string stepKey, CancellationToken ct = default)
        => await PostActionAsync($"/workflow-definitions/{definitionId}/steps/{Uri.EscapeDataString(stepKey)}/move-up", ct);

    public async Task<ApiActionResult> MoveStepDownAsync(
        Guid definitionId, string stepKey, CancellationToken ct = default)
        => await PostActionAsync($"/workflow-definitions/{definitionId}/steps/{Uri.EscapeDataString(stepKey)}/move-down", ct);

    public async Task<ApiActionResult> AddStepAsync(
        Guid definitionId,
        string stepKey,
        string stepType,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                $"/workflow-definitions/{definitionId}/steps",
                new { stepKey, stepType }, ct);

            if (response.IsSuccessStatusCode)
                return ApiActionResult.Ok();

            var body = await response.Content.ReadAsStringAsync(ct);
            return ApiActionResult.Fail(ExtractErrorMessage(body) ?? $"API returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding step to definition {Id}", definitionId);
            return ApiActionResult.Fail("An unexpected error occurred.");
        }
    }

    public async Task<ApiActionResult> UpdateStepAsync(
        Guid definitionId,
        string stepKey,
        UpdateStepRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PutAsJsonAsync(
                $"/workflow-definitions/{definitionId}/steps/{Uri.EscapeDataString(stepKey)}",
                request, ct);

            if (response.IsSuccessStatusCode)
                return ApiActionResult.Ok();

            var body = await response.Content.ReadAsStringAsync(ct);
            return ApiActionResult.Fail(ExtractErrorMessage(body) ?? $"API returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating step {StepKey} for definition {Id}", stepKey, definitionId);
            return ApiActionResult.Fail("An unexpected error occurred.");
        }
    }

    public async Task<ApiActionResult> ActivateDefinitionAsync(Guid id, CancellationToken ct = default)
        => await PostActionAsync($"/workflow-definitions/{id}/activate", ct);

    public async Task<ApiActionResult> DeactivateDefinitionAsync(Guid id, CancellationToken ct = default)
        => await PostActionAsync($"/workflow-definitions/{id}/deactivate", ct);

    public async Task<CloneDefinitionResult> CreateFromDescriptorAsync(
        string descriptorKey,
        int descriptorVersion,
        string name,
        string key,
        string triggerType,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/workflow-definitions/from-descriptor",
                new { descriptorKey, descriptorVersion, name, key, triggerType }, ct);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<CloneDefinitionResponse>(ct);
                return CloneDefinitionResult.Ok(body!.Id);
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            var message = ExtractErrorMessage(errorBody) ?? $"API returned {(int)response.StatusCode}.";
            return CloneDefinitionResult.Fail(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating workflow from descriptor {Key}", descriptorKey);
            return CloneDefinitionResult.Fail("An unexpected error occurred.");
        }
    }

    public async Task<CloneDefinitionResult> CreateBlankDefinitionAsync(
        string name,
        string key,
        string triggerType,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/workflow-definitions/blank",
                new { name, key, triggerType }, ct);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<CloneDefinitionResponse>(ct);
                return CloneDefinitionResult.Ok(body!.Id);
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            var message = ExtractErrorMessage(errorBody) ?? $"API returned {(int)response.StatusCode}.";
            return CloneDefinitionResult.Fail(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating blank definition");
            return CloneDefinitionResult.Fail("An unexpected error occurred.");
        }
    }

    public async Task<CloneDefinitionResult> CloneDefinitionAsync(
        Guid templateId,
        string name,
        string key,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/workflow-definitions/clone",
                new { templateId, name, key }, ct);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<CloneDefinitionResponse>(ct);
                return CloneDefinitionResult.Ok(body!.Id);
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            var message = ExtractErrorMessage(errorBody) ?? $"API returned {(int)response.StatusCode}.";
            return CloneDefinitionResult.Fail(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error cloning definition from template {TemplateId}", templateId);
            return CloneDefinitionResult.Fail("An unexpected error occurred. Check the application logs.");
        }
    }

    public async Task<CreateInstanceResult> CreateInstanceAsync(
        string workflowKey,
        string? externalKey,
        string? inputJson,
        CancellationToken ct = default)
    {
        var payload = new
        {
            workflowKey,
            tenantId = Services.TenantSeedService.DefaultTenantId,
            externalKey = string.IsNullOrWhiteSpace(externalKey) ? null : externalKey,
            input = string.IsNullOrWhiteSpace(inputJson)
                ? null
                : JsonSerializer.Deserialize<object>(inputJson)
        };

        try
        {
            var response = await _http.PostAsJsonAsync("/workflow-instances", payload, ct);
            if (response.IsSuccessStatusCode)
            {
                var created = await response.Content.ReadFromJsonAsync<StartWorkflowResponse>(ct);
                return CreateInstanceResult.Ok(created!.Id);
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            var message = ExtractErrorMessage(body) ?? $"API returned {(int)response.StatusCode}.";
            return CreateInstanceResult.Fail(message);
        }
        catch (JsonException)
        {
            return CreateInstanceResult.Fail("Input is not valid JSON.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating workflow instance");
            return CreateInstanceResult.Fail("An unexpected error occurred. Check the application logs.");
        }
    }

    public async Task<ApiActionResult> UpsertSecretAsync(
        string name,
        string value,
        string? description,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PutAsJsonAsync(
                $"/secrets/{Uri.EscapeDataString(name)}",
                new { value, description },
                ct);

            if (response.IsSuccessStatusCode)
                return ApiActionResult.Ok();

            var body = await response.Content.ReadAsStringAsync(ct);
            return ApiActionResult.Fail(ExtractErrorMessage(body) ?? $"API returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting secret {Name}", name);
            return ApiActionResult.Fail("An unexpected error occurred. Check the application logs.");
        }
    }

    public async Task<ApiActionResult> RetryAsync(Guid id, CancellationToken ct = default)
        => await PostActionAsync($"/workflow-instances/{id}/retry", ct);

    public async Task<ApiActionResult> ReplayAsync(Guid id, CancellationToken ct = default)
        => await PostActionAsync($"/workflow-instances/{id}/replay", ct);

    public async Task<ApiActionResult> CancelAsync(Guid id, CancellationToken ct = default)
        => await PostActionAsync($"/workflow-instances/{id}/cancel", ct);

    public async Task<ApiActionResult> ArchiveAsync(Guid id, CancellationToken ct = default)
        => await PostActionAsync($"/workflow-instances/{id}/archive", ct);

    private async Task<ApiActionResult> PostActionAsync(string url, CancellationToken ct)
    {
        try
        {
            var response = await _http.PostAsync(url, content: null, ct);

            if (response.IsSuccessStatusCode)
                return ApiActionResult.Ok();

            var body = await response.Content.ReadAsStringAsync(ct);
            var message = ExtractErrorMessage(body) ?? $"API returned {(int)response.StatusCode}.";
            return ApiActionResult.Fail(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error calling {Url}", url);
            return ApiActionResult.Fail("An unexpected error occurred. Check the application logs.");
        }
    }

    private static string? ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);

            var message = doc.RootElement.TryGetProperty("error", out var errorProp)
                ? errorProp.GetString()
                : null;

            // Append detailed validation errors if present (e.g., from activation).
            if (doc.RootElement.TryGetProperty("errors", out var errorsList) &&
                errorsList.ValueKind == JsonValueKind.Array)
            {
                var details = errorsList.EnumerateArray()
                    .Select(e => e.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                if (details.Count > 0)
                {
                    var detailText = string.Join(" ", details.Select(d => $"- {d}"));
                    message = message is not null
                        ? $"{message} {detailText}"
                        : detailText;
                }
            }

            return message;
        }
        catch { /* not JSON — fall through */ }
        return null;
    }
}

// ── Result types ─────────────────────────────────────────────────────────────────

public sealed class ApiActionResult
{
    public bool Success { get; private init; }
    public string? ErrorMessage { get; private init; }

    public static ApiActionResult Ok() => new() { Success = true };
    public static ApiActionResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}

public sealed class CreateInstanceResult
{
    public bool Success { get; private init; }
    public Guid InstanceId { get; private init; }
    public string? ErrorMessage { get; private init; }

    public static CreateInstanceResult Ok(Guid id) => new() { Success = true, InstanceId = id };
    public static CreateInstanceResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}

public sealed class CloneDefinitionResult
{
    public bool Success { get; private init; }
    public Guid DefinitionId { get; private init; }
    public string? ErrorMessage { get; private init; }

    public static CloneDefinitionResult Ok(Guid id) => new() { Success = true, DefinitionId = id };
    public static CloneDefinitionResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}

public sealed class CloneDefinitionResponse
{
    public Guid Id { get; init; }
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}

// ── API response DTOs used only by the client ────────────────────────────────────

public sealed class WorkflowDescriptorSummary
{
    public string Key { get; init; } = string.Empty;
    public int Version { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public IReadOnlyList<WorkflowDescriptorStepSummary> Steps { get; init; } = [];
}

public sealed class WorkflowDescriptorStepSummary
{
    public int Order { get; init; }
    public string StepKey { get; init; } = string.Empty;
    public string StepType { get; init; } = string.Empty;
}
