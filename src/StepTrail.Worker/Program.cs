using StepTrail.Shared;
using StepTrail.Shared.Workflows;
using StepTrail.Worker;
using StepTrail.Worker.Alerts;
using StepTrail.Worker.Handlers;
using StepTrail.Worker.StepExecutors;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddStepTrailDb(builder.Configuration);
builder.Services.AddWorkflowRegistry();

builder.Services.AddHttpClient("HttpActivity", client =>
    client.Timeout = Timeout.InfiniteTimeSpan); // timeouts are driven by the step's CancellationToken

builder.Services.AddHttpClient("AlertWebhook", client =>
    client.Timeout = TimeSpan.FromSeconds(10));

// Console log channel is always active.
// Webhook channel is registered only when Alerts:WebhookUrl is configured.
builder.Services.AddScoped<IAlertChannel, ConsoleLogAlertChannel>();
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetValue<string>("Alerts:WebhookUrl")))
    builder.Services.AddScoped<IAlertChannel, WebhookAlertChannel>();

builder.Services.AddScoped<AlertService>();
builder.Services.AddSingleton(AlertRuleEvaluator.CreateDefault());
builder.Services.AddScoped<StepTrail.Shared.Telemetry.TelemetryService>();

builder.Services.AddScoped<StepExecutionClaimer>();
builder.Services.AddScoped<StepFailureService>();
builder.Services.AddScoped<StepExecutionProcessor>();
builder.Services.AddScoped<StuckExecutionDetector>();
builder.Services.AddScoped<RecurringWorkflowDispatcher>();

builder.Services.AddWorkerStepExecutors();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
