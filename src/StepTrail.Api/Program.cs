using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using StepTrail.Api.Endpoints;
using StepTrail.Api.Services;
using StepTrail.Api.UI;
using StepTrail.Api.Workflows;
using StepTrail.Shared;
using StepTrail.Shared.Telemetry;

var builder = WebApplication.CreateBuilder(args);

// ── Authentication & Authorization ──────────────────────────────────────────
builder.Services.AddOpenApi();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath        = "/login";
        options.AccessDeniedPath = "/login";
        options.Cookie.HttpOnly    = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan    = TimeSpan.FromHours(8);
        options.SlidingExpiration  = true;
    });

builder.Services.AddAuthorization();

builder.Services.AddRazorPages(options =>
    options.Conventions.AuthorizeFolder("/"));

// ── HTTP clients ────────────────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<ForwardAuthCookieHandler>();

builder.Services.AddHttpClient<WorkflowApiClient>(client =>
{
    var baseUrl = builder.Configuration["UI:ApiBaseUrl"] ?? "http://localhost:5000";
    client.BaseAddress = new Uri(baseUrl);
}).AddHttpMessageHandler<ForwardAuthCookieHandler>();

// ── Data & workflow registration ────────────────────────────────────────────
builder.Services.AddStepTrailDb(builder.Configuration, migrationsAssembly: "StepTrail.Api");

builder.Services.AddWorkflow<WebhookTransformForwardWorkflow>();
builder.Services.AddWorkflow<WebhookMultiStepApiChainWorkflow>();
builder.Services.AddWorkflow<ScheduledHttpCheckAlertWorkflow>();
builder.Services.AddWorkflowRegistry();

// ── Hosted services ─────────────────────────────────────────────────────────
builder.Services.AddHostedService<TenantSeedService>();
builder.Services.AddHostedService<WorkflowDefinitionSyncService>();

if (builder.Environment.IsDevelopment())
    builder.Services.AddHostedService<DevDataSeedService>();

// ── Application services ────────────────────────────────────────────────────
builder.Services.AddScoped<WorkflowInstanceService>();
builder.Services.AddScoped<ExecutableWorkflowTriggerResolver>();
builder.Services.AddScoped<ManualWorkflowTriggerService>();
builder.Services.AddScoped<WebhookIdempotencyKeyExtractor>();
builder.Services.AddScoped<WebhookInputMapper>();
builder.Services.AddScoped<WebhookSignatureValidationService>();
builder.Services.AddScoped<WebhookWorkflowTriggerService>();
builder.Services.AddScoped<WorkflowRetryService>();
builder.Services.AddScoped<WorkflowQueryService>();
builder.Services.AddScoped<TelemetryService>();

// ── Build & configure pipeline ──────────────────────────────────────────────
var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapRazorPages();

// Apply pending EF Core migrations on startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StepTrailDbContext>();
    await db.Database.MigrateAsync();
}

// ── Map endpoints ───────────────────────────────────────────────────────────
app.MapPublicEndpoints();

var ops = app.MapGroup("").RequireAuthorization();
ops.MapDefinitionEndpoints();
ops.MapInstanceEndpoints();
ops.MapOpsEndpoints();

app.Run();
