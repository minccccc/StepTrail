using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace StepTrail.TestLab;

public sealed class StepTrailTriggerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<TestLabOptions> _options;
    private readonly LabStateStore _stateStore;

    public StepTrailTriggerClient(
        IHttpClientFactory httpClientFactory,
        IOptions<TestLabOptions> options,
        LabStateStore stateStore)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _stateStore = stateStore;
    }

    public async Task<(Guid? instanceId, string summary)> TriggerAsync(string scenarioName, CancellationToken ct)
    {
        var normalizedScenario = _stateStore.ActivateScenario(scenarioName);
        var client = _httpClientFactory.CreateClient("StepTrailApi");

        var externalKey = $"{normalizedScenario}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var payload = new
        {
            id = $"req-{DateTimeOffset.UtcNow:HHmmss}",
            action = normalizedScenario == LabScenarioNames.HappyPath ? "sync" : "retry-demo",
            labScenario = normalizedScenario,
            payload = new
            {
                customerId = $"cust-{DateTimeOffset.UtcNow:ddHHmm}",
                source = "testlab"
            }
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.Value.StepTrailApiBaseUrl.TrimEnd('/')}/webhooks/{TestLabDefaults.WorkflowRouteKey}")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.TryAddWithoutValidation("X-External-Key", externalKey);

        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Guid? instanceId = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var startResponse = JsonSerializer.Deserialize<StartWorkflowResponseDto>(body, JsonOptions);
                instanceId = startResponse?.Id;
            }
            catch (JsonException)
            {
            }
        }

        var summary = response.IsSuccessStatusCode
            ? $"Triggered '{normalizedScenario}' via /webhooks/{TestLabDefaults.WorkflowRouteKey} ({(int)response.StatusCode})."
            : $"StepTrail trigger failed with {(int)response.StatusCode}: {body}";

        _stateStore.NoteTrigger(instanceId, summary);
        return (instanceId, summary);
    }

    private sealed class StartWorkflowResponseDto
    {
        public Guid Id { get; set; }
    }
}
