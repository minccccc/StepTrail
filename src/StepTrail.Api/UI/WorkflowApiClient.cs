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
        bool includeArchived = false,
        int page = 1,
        int pageSize = 100,
        CancellationToken ct = default)
    {
        var url = $"/workflow-instances?page={page}&pageSize={pageSize}&includeArchived={includeArchived}";
        if (!string.IsNullOrWhiteSpace(status))
            url += $"&status={Uri.EscapeDataString(status)}";

        var result = await _http.GetFromJsonAsync<PagedResult<WorkflowInstanceSummary>>(url, ct);
        return result ?? new PagedResult<WorkflowInstanceSummary>();
    }

    public async Task<IReadOnlyList<WorkflowDescriptorSummary>> ListWorkflowsAsync(
        CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<List<WorkflowDescriptorSummary>>("/workflows", ct);
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
            if (doc.RootElement.TryGetProperty("error", out var errorProp))
                return errorProp.GetString();
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

// ── API response DTOs used only by the client ────────────────────────────────────

public sealed class WorkflowDescriptorSummary
{
    public string Key { get; init; } = string.Empty;
    public int Version { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}
