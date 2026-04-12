using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using StepTrail.Api.Models;
using StepTrail.Api.Services;
using StepTrail.Api.UI;
using StepTrail.Api.Workflows;
using StepTrail.Shared;
using StepTrail.Shared.Workflows;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddRazorPages();

// Typed HTTP client for Razor Pages — calls the same API process via loopback
builder.Services.AddHttpClient<WorkflowApiClient>(client =>
{
    var baseUrl = builder.Configuration["UI:ApiBaseUrl"] ?? "http://localhost:5000";
    client.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddStepTrailDb(builder.Configuration, migrationsAssembly: "StepTrail.Api");

builder.Services.AddWorkflow<UserOnboardingWorkflow>();
builder.Services.AddWorkflowRegistry();

builder.Services.AddHostedService<TenantSeedService>();
builder.Services.AddHostedService<WorkflowDefinitionSyncService>();

if (builder.Environment.IsDevelopment())
    builder.Services.AddHostedService<DevDataSeedService>();

builder.Services.AddScoped<WorkflowInstanceService>();
builder.Services.AddScoped<WorkflowRetryService>();
builder.Services.AddScoped<WorkflowQueryService>();

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapRazorPages();

// Apply any pending EF Core migrations on startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StepTrailDbContext>();
    await db.Database.MigrateAsync();
}

app.MapGet("/health", async (StepTrailDbContext db) =>
{
    try
    {
        await db.Database.CanConnectAsync();
        return Results.Ok(new { status = "healthy", database = "connected" });
    }
    catch (Exception ex)
    {
        return Results.Json(
            new { status = "unhealthy", database = "disconnected", error = ex.Message },
            statusCode: 503);
    }
});

app.MapGet("/workflows", (IWorkflowRegistry registry) =>
{
    var workflows = registry.GetAll().Select(w => new
    {
        w.Key,
        w.Version,
        w.Name,
        w.Description,
        Steps = w.Steps
            .OrderBy(s => s.Order)
            .Select(s => new { s.Order, s.StepKey, s.StepType })
    });
    return Results.Ok(workflows);
});

app.MapPost("/workflow-instances", async (
    StartWorkflowRequest request,
    WorkflowInstanceService service,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.WorkflowKey))
        return Results.BadRequest(new { error = "WorkflowKey is required." });

    if (request.TenantId == Guid.Empty)
        return Results.BadRequest(new { error = "TenantId is required." });

    try
    {
        var (response, created) = await service.StartAsync(request, ct);
        return created
            ? Results.Created($"/workflow-instances/{response.Id}", response)
            : Results.Ok(response);
    }
    catch (WorkflowNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (TenantNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
});

app.MapGet("/workflow-instances", async (
    Guid? tenantId,
    string? workflowKey,
    string? status,
    bool? includeArchived,
    int? page,
    int? pageSize,
    WorkflowQueryService service,
    CancellationToken ct) =>
{
    var effectivePage = Math.Max(page ?? 1, 1);
    var effectivePageSize = Math.Clamp(pageSize ?? 20, 1, 100);
    try
    {
        var result = await service.ListAsync(
            tenantId, workflowKey, status,
            includeArchived ?? false,
            effectivePage, effectivePageSize, ct);
        return Results.Ok(result);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/workflow-instances/{id:guid}", async (
    Guid id,
    WorkflowQueryService service,
    CancellationToken ct) =>
{
    try
    {
        var result = await service.GetDetailAsync(id, ct);
        return Results.Ok(result);
    }
    catch (WorkflowInstanceNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
});

app.MapGet("/workflow-instances/{id:guid}/timeline", async (
    Guid id,
    WorkflowQueryService service,
    CancellationToken ct) =>
{
    try
    {
        var result = await service.GetTimelineAsync(id, ct);
        return Results.Ok(result);
    }
    catch (WorkflowInstanceNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
});

app.MapPost("/workflow-instances/{id:guid}/retry", async (
    Guid id,
    WorkflowRetryService service,
    CancellationToken ct) =>
{
    try
    {
        var response = await service.RetryAsync(id, ct);
        return Results.Ok(response);
    }
    catch (WorkflowInstanceNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (InvalidWorkflowStateException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

app.MapPost("/workflow-instances/{id:guid}/replay", async (
    Guid id,
    WorkflowRetryService service,
    CancellationToken ct) =>
{
    try
    {
        var response = await service.ReplayAsync(id, ct);
        return Results.Ok(response);
    }
    catch (WorkflowInstanceNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (InvalidWorkflowStateException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

app.MapPost("/workflow-instances/{id:guid}/archive", async (
    Guid id,
    WorkflowRetryService service,
    CancellationToken ct) =>
{
    try
    {
        var response = await service.ArchiveAsync(id, ct);
        return Results.Ok(response);
    }
    catch (WorkflowInstanceNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (InvalidWorkflowStateException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

app.MapPost("/workflow-instances/{id:guid}/cancel", async (
    Guid id,
    WorkflowRetryService service,
    CancellationToken ct) =>
{
    try
    {
        var response = await service.CancelAsync(id, ct);
        return Results.Ok(response);
    }
    catch (WorkflowInstanceNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (InvalidWorkflowStateException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

app.Run();
