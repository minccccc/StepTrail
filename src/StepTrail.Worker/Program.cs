using StepTrail.Shared;
using StepTrail.Shared.Workflows;
using StepTrail.Worker;
using StepTrail.Worker.Alerts;
using StepTrail.Worker.Handlers;

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

builder.Services.AddScoped<StepExecutionClaimer>();
builder.Services.AddScoped<StepFailureService>();
builder.Services.AddScoped<StepExecutionProcessor>();
builder.Services.AddScoped<StuckExecutionDetector>();
builder.Services.AddScoped<RecurringWorkflowDispatcher>();

builder.Services.AddKeyedScoped<IStepHandler, SendWelcomeEmailHandler>(nameof(SendWelcomeEmailHandler));
builder.Services.AddKeyedScoped<IStepHandler, ProvisionAccountHandler>(nameof(ProvisionAccountHandler));
builder.Services.AddKeyedScoped<IStepHandler, NotifyTeamHandler>(nameof(NotifyTeamHandler));
builder.Services.AddKeyedScoped<IStepHandler, HttpActivityHandler>(nameof(HttpActivityHandler));

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
