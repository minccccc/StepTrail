using StepTrail.Shared;
using StepTrail.Shared.Workflows;
using StepTrail.Worker;
using StepTrail.Worker.Handlers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddStepTrailDb(builder.Configuration);
builder.Services.AddWorkflowRegistry();

builder.Services.AddScoped<StepExecutionClaimer>();
builder.Services.AddScoped<StepExecutionProcessor>();

builder.Services.AddKeyedScoped<IStepHandler, SendWelcomeEmailHandler>(nameof(SendWelcomeEmailHandler));
builder.Services.AddKeyedScoped<IStepHandler, ProvisionAccountHandler>(nameof(ProvisionAccountHandler));
builder.Services.AddKeyedScoped<IStepHandler, NotifyTeamHandler>(nameof(NotifyTeamHandler));

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
