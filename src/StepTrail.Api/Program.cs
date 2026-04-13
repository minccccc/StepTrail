using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using StepTrail.Shared.Entities;
using Scalar.AspNetCore;
using StepTrail.Api.Models;
using StepTrail.Api.Services;
using StepTrail.Api.UI;
using StepTrail.Api.Workflows;
using StepTrail.Shared;
using StepTrail.Shared.Workflows;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath      = "/login";
        options.AccessDeniedPath = "/login";
        options.Cookie.HttpOnly   = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan   = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

// Protect all Razor Pages by default; Login is exempt via [AllowAnonymous].
builder.Services.AddRazorPages(options =>
    options.Conventions.AuthorizeFolder("/"));

// IHttpContextAccessor is needed by ForwardAuthCookieHandler so it can read the
// current request's cookie and forward it on loopback calls to the ops API.
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<ForwardAuthCookieHandler>();

// Typed HTTP client for Razor Pages — calls the same API process via loopback.
// ForwardAuthCookieHandler copies the browser's auth cookie onto each outbound request
// so that .RequireAuthorization() on the API endpoints is satisfied.
builder.Services.AddHttpClient<WorkflowApiClient>(client =>
{
    var baseUrl = builder.Configuration["UI:ApiBaseUrl"] ?? "http://localhost:5000";
    client.BaseAddress = new Uri(baseUrl);
}).AddHttpMessageHandler<ForwardAuthCookieHandler>();

builder.Services.AddStepTrailDb(builder.Configuration, migrationsAssembly: "StepTrail.Api");

builder.Services.AddWorkflow<UserOnboardingWorkflow>();
builder.Services.AddWorkflow<WebhookToHttpCallWorkflow>();
builder.Services.AddWorkflowRegistry();

builder.Services.AddHostedService<TenantSeedService>();
builder.Services.AddHostedService<WorkflowDefinitionSyncService>();

if (builder.Environment.IsDevelopment())
    builder.Services.AddHostedService<DevDataSeedService>();

builder.Services.AddScoped<WorkflowInstanceService>();
builder.Services.AddScoped<WorkflowRetryService>();
builder.Services.AddScoped<WorkflowQueryService>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapRazorPages();

app.MapPost("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).AllowAnonymous();

// Apply any pending EF Core migrations on startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StepTrailDbContext>();
    await db.Database.MigrateAsync();
}

// ── Public endpoints ──────────────────────────────────────────────────────────

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

// Webhook endpoint — designed for external callers; intentionally unauthenticated.
// Workflow key comes from the URL; the full request body is stored as workflow input.
// Idempotency and correlation keys are supplied as standard HTTP headers.
// TenantId query param is optional; omit it to use the default tenant.
app.MapPost("/webhooks/{workflowKey}", async (
    string workflowKey,
    HttpRequest httpRequest,
    WorkflowInstanceService service,
    CancellationToken ct) =>
{
    var idempotencyKey = httpRequest.Headers["X-Idempotency-Key"].FirstOrDefault();
    var externalKey    = httpRequest.Headers["X-External-Key"].FirstOrDefault();
    var tenantId = Guid.TryParse(httpRequest.Query["tenantId"].FirstOrDefault(), out var tid)
        ? tid
        : TenantSeedService.DefaultTenantId;

    // Read body as JSON if the caller sent one; null otherwise.
    object? input = null;
    if (httpRequest.HasJsonContentType())
    {
        try { input = await httpRequest.ReadFromJsonAsync<JsonElement>(ct); }
        catch (JsonException) { /* malformed body — proceed with no input */ }
    }

    var request = new StartWorkflowRequest
    {
        WorkflowKey    = workflowKey,
        TenantId       = tenantId,
        ExternalKey    = externalKey,
        IdempotencyKey = idempotencyKey,
        Input          = input
    };

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

// ── Ops API — requires authentication ────────────────────────────────────────
// All endpoints below are internal operations endpoints. Cookie authentication is
// required. The Razor Pages UI satisfies this via ForwardAuthCookieHandler on
// WorkflowApiClient; direct callers must present a valid session cookie.

var ops = app.MapGroup("").RequireAuthorization();

ops.MapGet("/workflows", (IWorkflowRegistry registry) =>
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

ops.MapPost("/workflow-instances", async (
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

ops.MapGet("/workflow-instances", async (
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

ops.MapGet("/workflow-instances/{id:guid}", async (
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

ops.MapGet("/workflow-instances/{id:guid}/timeline", async (
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

ops.MapPost("/workflow-instances/{id:guid}/retry", async (
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

ops.MapPost("/workflow-instances/{id:guid}/replay", async (
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

ops.MapPost("/workflow-instances/{id:guid}/archive", async (
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

ops.MapPost("/workflow-instances/{id:guid}/cancel", async (
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

// ── Secrets management ────────────────────────────────────────────────────────
// Values are never returned by the API; only names and descriptions are exposed.

ops.MapGet("/secrets", async (StepTrailDbContext db, CancellationToken ct) =>
{
    var secrets = await db.WorkflowSecrets
        .OrderBy(s => s.Name)
        .Select(s => new { s.Name, s.Description, s.UpdatedAt })
        .ToListAsync(ct);
    return Results.Ok(secrets);
});

ops.MapPut("/secrets/{name}", async (
    string name,
    UpsertSecretRequest req,
    StepTrailDbContext db,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Value))
        return Results.BadRequest(new { error = "Value is required." });

    var existing = await db.WorkflowSecrets
        .FirstOrDefaultAsync(s => s.Name == name, ct);

    var now = DateTimeOffset.UtcNow;

    if (existing is null)
    {
        db.WorkflowSecrets.Add(new WorkflowSecret
        {
            Id          = Guid.NewGuid(),
            Name        = name,
            Value       = req.Value,
            Description = req.Description,
            CreatedAt   = now,
            UpdatedAt   = now
        });
        await db.SaveChangesAsync(ct);
        return Results.Created($"/secrets/{name}", new { name });
    }

    existing.Value       = req.Value;
    existing.Description = req.Description ?? existing.Description;
    existing.UpdatedAt   = now;
    await db.SaveChangesAsync(ct);
    return Results.Ok(new { name });
});

ops.MapDelete("/secrets/{name}", async (
    string name,
    StepTrailDbContext db,
    CancellationToken ct) =>
{
    var secret = await db.WorkflowSecrets
        .FirstOrDefaultAsync(s => s.Name == name, ct);

    if (secret is null) return Results.NotFound(new { error = $"Secret '{name}' not found." });

    db.WorkflowSecrets.Remove(secret);
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});

app.Run();
