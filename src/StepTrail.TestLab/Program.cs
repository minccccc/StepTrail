using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StepTrail.Shared;
using StepTrail.TestLab;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<TestLabOptions>(builder.Configuration.GetSection("TestLab"));
builder.Services.AddStepTrailDb(builder.Configuration);
builder.Services.AddWorkflowRegistry();
builder.Services.AddHttpClient("StepTrailApi");
builder.Services.AddSingleton<LabStateStore>();
builder.Services.AddSingleton<HtmlPageRenderer>();
builder.Services.AddSingleton<TestLabBootstrapper>();
builder.Services.AddScoped<StepTrailTriggerClient>();
builder.Services.AddHostedService<TestLabStartupService>();

var app = builder.Build();

app.MapGet("/", (HttpRequest request, LabStateStore stateStore, HtmlPageRenderer renderer) =>
{
    var page = renderer.Render(stateStore.Snapshot(), request.Query["message"]);
    return Results.Content(page, "text/html");
});

app.MapGet("/lab/status", (LabStateStore stateStore) =>
    Results.Ok(stateStore.Snapshot()));

app.MapGet("/lab/requests", (LabStateStore stateStore) =>
    Results.Ok(stateStore.Snapshot().Requests));

app.MapPost("/lab/setup", async (
    TestLabBootstrapper bootstrapper,
    CancellationToken ct) =>
{
    var status = await bootstrapper.EnsureDemoAssetsAsync(ct);
    return Results.Redirect("/?message=" + Uri.EscapeDataString(status));
});

app.MapPost("/lab/reset", (LabStateStore stateStore) =>
{
    stateStore.ResetActivity();
    return Results.Redirect("/?message=" + Uri.EscapeDataString("Lab activity reset."));
});

app.MapPost("/lab/trigger/{scenarioName}", async (
    string scenarioName,
    TestLabBootstrapper bootstrapper,
    StepTrailTriggerClient triggerClient,
    CancellationToken ct) =>
{
    if (!LabScenarioNames.IsKnown(scenarioName))
        return Results.Redirect("/?message=" + Uri.EscapeDataString($"Unknown scenario '{scenarioName}'."));

    await bootstrapper.EnsureDemoAssetsAsync(ct);
    var (_, summary) = await triggerClient.TriggerAsync(scenarioName, ct);
    return Results.Redirect("/?message=" + Uri.EscapeDataString(summary));
});

app.MapPost("/mock/api-a", async Task<IResult> (
    HttpRequest request,
    LabStateStore stateStore,
    CancellationToken ct) =>
{
    var body = await ReadBodyAsync(request, ct);
    var callNumber = stateStore.NextApiACall();
    var scenario = ExtractScenario(body, stateStore.GetActiveScenario());

    var response = new
    {
        id = $"api-a-{callNumber:000}",
        status = "accepted",
        downstream = "api-a",
        scenario
    };

    stateStore.RecordRequest(new LabRequestRecord(
        DateTimeOffset.UtcNow,
        scenario,
        "/mock/api-a",
        request.Method,
        (int)HttpStatusCode.OK,
        body));

    return Results.Json(response, statusCode: StatusCodes.Status200OK);
});

app.MapPost("/mock/api-b", async Task<IResult> (
    HttpRequest request,
    LabStateStore stateStore,
    CancellationToken ct) =>
{
    var body = await ReadBodyAsync(request, ct);
    var callNumber = stateStore.NextApiBCall();
    var scenario = ExtractScenario(body, stateStore.GetActiveScenario());

    var statusCode = ResolveApiBStatusCode(scenario, callNumber);
    stateStore.RecordRequest(new LabRequestRecord(
        DateTimeOffset.UtcNow,
        scenario,
        "/mock/api-b",
        request.Method,
        statusCode,
        body));

    if (statusCode >= 400)
    {
        return Results.Json(new
        {
            error = "Synthetic downstream failure from TestLab.",
            scenario,
            attempt = callNumber
        }, statusCode: statusCode);
    }

    return Results.Json(new
    {
        delivered = true,
        scenario,
        attempt = callNumber
    }, statusCode: StatusCodes.Status200OK);
});

app.Run();
return;

static int ResolveApiBStatusCode(string scenario, int callNumber) =>
    scenario switch
    {
        LabScenarioNames.FailThenRecover when callNumber < 3 => StatusCodes.Status502BadGateway,
        LabScenarioNames.PermanentFailure => StatusCodes.Status500InternalServerError,
        _ => StatusCodes.Status200OK
    };

static string ExtractScenario(string? body, string fallbackScenario)
{
    if (string.IsNullOrWhiteSpace(body))
        return fallbackScenario;

    try
    {
        using var document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("scenario", out var scenarioElement) &&
            scenarioElement.ValueKind == JsonValueKind.String)
        {
            return LabScenarioNames.Normalize(scenarioElement.GetString());
        }
    }
    catch (JsonException)
    {
    }

    return fallbackScenario;
}

static async Task<string> ReadBodyAsync(HttpRequest request, CancellationToken ct)
{
    using var reader = new StreamReader(request.Body);
    return await reader.ReadToEndAsync(ct);
}
