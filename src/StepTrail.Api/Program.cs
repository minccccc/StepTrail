using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StepTrail.Shared.Entities;
using Scalar.AspNetCore;
using StepTrail.Api.Models;
using StepTrail.Api.Services;
using StepTrail.Api.UI;
using StepTrail.Api.Workflows;
using StepTrail.Shared;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Runtime.AvailableFields;
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
builder.Services.Configure<ApiTriggerAuthenticationOptions>(
    builder.Configuration.GetSection(ApiTriggerAuthenticationOptions.SectionName));

builder.Services.AddWorkflow<UserOnboardingWorkflow>();
builder.Services.AddWorkflow<WebhookToHttpCallWorkflow>();
builder.Services.AddWorkflowRegistry();

builder.Services.AddHostedService<TenantSeedService>();
builder.Services.AddHostedService<WorkflowDefinitionSyncService>();

if (builder.Environment.IsDevelopment())
    builder.Services.AddHostedService<DevDataSeedService>();

builder.Services.AddScoped<WorkflowInstanceService>(sp =>
    new WorkflowInstanceService(sp.GetRequiredService<StepTrail.Shared.Runtime.WorkflowStartService>()));
builder.Services.AddScoped<ExecutableWorkflowTriggerResolver>();
builder.Services.AddScoped<ManualWorkflowTriggerService>();
builder.Services.AddScoped<ApiTriggerAuthenticationService>();
builder.Services.AddScoped<ApiWorkflowTriggerService>();
builder.Services.AddScoped<WebhookIdempotencyKeyExtractor>();
builder.Services.AddScoped<WebhookInputMapper>();
builder.Services.AddScoped<WebhookSignatureValidationService>();
builder.Services.AddScoped<WebhookWorkflowTriggerService>();
builder.Services.AddScoped<WorkflowRetryService>();
builder.Services.AddScoped<WorkflowQueryService>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

var apiTriggerAuthenticationOptions = app.Services
    .GetRequiredService<IOptions<ApiTriggerAuthenticationOptions>>()
    .Value;
if (string.IsNullOrWhiteSpace(apiTriggerAuthenticationOptions.SharedSecret))
{
    if (apiTriggerAuthenticationOptions.AllowUnauthenticated)
    {
        app.Logger.LogWarning(
            "API trigger authentication is explicitly disabled because {Section}:{Property} is true and no shared secret is configured. This should only be used in local development.",
            ApiTriggerAuthenticationOptions.SectionName,
            nameof(ApiTriggerAuthenticationOptions.AllowUnauthenticated));
    }
    else
    {
        app.Logger.LogWarning(
            "API trigger authentication is not configured. API trigger requests will be rejected until {SharedSecretSetting} is set or {AllowUnauthenticatedSetting} is explicitly enabled.",
            $"{ApiTriggerAuthenticationOptions.SectionName}:SharedSecret",
            $"{ApiTriggerAuthenticationOptions.SectionName}:{nameof(ApiTriggerAuthenticationOptions.AllowUnauthenticated)}");
    }
}

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
// Route key comes from the URL and resolves to one active webhook-triggered workflow definition.
// The first intake version requires JSON and uses the parsed body as normalized input while also
// preserving the raw request body in trigger_data for debugging and later validation work.
// Idempotency and correlation keys are supplied as standard HTTP headers.
// TenantId query param is optional; omit it to use the default tenant.
app.MapMethods("/webhooks/{routeKey}", [HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete, HttpMethods.Head, HttpMethods.Options], async (
    string routeKey,
    HttpRequest httpRequest,
    WebhookWorkflowTriggerService service,
    CancellationToken ct) =>
{
    if (!httpRequest.HasJsonContentType())
        return Results.BadRequest(new { error = "Webhook requests must use a JSON content type." });

    string rawBody;
    using (var reader = new StreamReader(httpRequest.Body))
    {
        rawBody = await reader.ReadToEndAsync(ct);
    }

    if (string.IsNullOrWhiteSpace(rawBody))
        return Results.BadRequest(new { error = "Webhook request body is required." });

    JsonElement payload;
    try
    {
        payload = JsonSerializer.Deserialize<JsonElement>(rawBody);
    }
    catch (JsonException ex)
    {
        return Results.BadRequest(new { error = $"Webhook request body must be valid JSON. {ex.Message}" });
    }

    var externalKey    = httpRequest.Headers["X-External-Key"].FirstOrDefault();
    var tenantId = Guid.TryParse(httpRequest.Query["tenantId"].FirstOrDefault(), out var tid)
        ? tid
        : TenantSeedService.DefaultTenantId;

    try
    {
        var request = new StartWebhookWorkflowRequest
        {
            RouteKey = routeKey,
            TenantId = tenantId,
            ExternalKey = externalKey,
            HttpMethod = httpRequest.Method,
            RawBody = rawBody,
            Payload = payload,
            Headers = CaptureRequestHeaders(httpRequest),
            Query = httpRequest.Query
                .Where(q => !string.Equals(q.Key, "tenantId", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(q => q.Key, q => q.Value.ToString())
        };

        var (response, created) = await service.StartAsync(request, ct);
        return created
            ? Results.Accepted($"/workflow-instances/{response.Id}", response)
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
    catch (WebhookTriggerPayloadInvalidException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (WebhookTriggerInputMappingException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (WebhookTriggerIdempotencyExtractionException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (WebhookTriggerMethodNotAllowedException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status405MethodNotAllowed);
    }
    catch (WebhookTriggerSignatureValidationException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status401Unauthorized);
    }
    catch (WebhookTriggerSignatureConfigurationException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
    }
    catch (WorkflowDefinitionNotActiveException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
    catch (WorkflowTriggerMismatchException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api-triggers/{workflowKey}", async (
    string workflowKey,
    int? version,
    HttpRequest httpRequest,
    ApiTriggerAuthenticationService authenticationService,
    ApiWorkflowTriggerService service,
    CancellationToken ct) =>
{
    if (!httpRequest.HasJsonContentType())
        return Results.BadRequest(new { error = "API trigger requests must use a JSON content type." });

    string rawBody;
    using (var reader = new StreamReader(httpRequest.Body))
    {
        rawBody = await reader.ReadToEndAsync(ct);
    }

    if (string.IsNullOrWhiteSpace(rawBody))
        return Results.BadRequest(new { error = "Request body is required." });

    JsonElement payload;
    try
    {
        payload = JsonSerializer.Deserialize<JsonElement>(rawBody);
    }
    catch (JsonException ex)
    {
        return Results.BadRequest(new { error = $"Request body must be valid JSON. {ex.Message}" });
    }

    var idempotencyKey = httpRequest.Headers["X-Idempotency-Key"].FirstOrDefault();
    var externalKey    = httpRequest.Headers["X-External-Key"].FirstOrDefault();
    var tenantId = Guid.TryParse(httpRequest.Query["tenantId"].FirstOrDefault(), out var tid)
        ? tid
        : TenantSeedService.DefaultTenantId;

    var request = new StartApiWorkflowRequest
    {
        WorkflowKey    = workflowKey,
        Version        = version,
        TenantId       = tenantId,
        ExternalKey    = externalKey,
        IdempotencyKey = idempotencyKey,
        ApiKey         = httpRequest.Headers[authenticationService.HeaderName].FirstOrDefault(),
        Payload        = payload,
        Headers        = CaptureRequestHeaders(httpRequest),
        Query          = httpRequest.Query
            .Where(q => !string.Equals(q.Key, "tenantId", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(q => q.Key, q => q.Value.ToString())
    };

    try
    {
        var (response, created) = await service.StartAsync(request, ct);
        return created
            ? Results.Accepted($"/workflow-instances/{response.Id}", response)
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
    catch (WorkflowDefinitionNotActiveException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
    catch (WorkflowTriggerMismatchException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (ApiTriggerAuthenticationException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status401Unauthorized);
    }
    catch (ApiTriggerAuthenticationConfigurationException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
    }
});

// ── Ops API — requires authentication ────────────────────────────────────────
// All endpoints below are internal operations endpoints. Cookie authentication is
// required. The Razor Pages UI satisfies this via ForwardAuthCookieHandler on
// WorkflowApiClient; direct callers must present a valid session cookie.

var ops = app.MapGroup("").RequireAuthorization();

// ── Workflow definition endpoints ─────────────────────────────────────────────

/// <summary>
/// Returns all placeholder paths available when configuring a specific step.
///
/// Results include:
///   - an input note ({{input.*}} guidance — no schema enumeration)
///   - per-step output fields for every step that precedes <paramref name="stepKey"/> in execution order
///   - all registered secrets as {{secrets.*}} references
///
/// Query parameter <c>version</c> is optional; omit it to query the active version.
/// </summary>
ops.MapGet("/workflow-definitions/{key}/steps/{stepKey}/available-fields", async (
    string key,
    string stepKey,
    int? version,
    IWorkflowDefinitionRepository repository,
    StepTrailDbContext db,
    CancellationToken ct) =>
{
    var definition = version.HasValue
        ? await repository.GetByKeyAndVersionAsync(key, version.Value, ct)
        : await repository.GetActiveByKeyAsync(key, ct);

    if (definition is null)
    {
        var versionSuffix = version.HasValue ? $" (version {version.Value})" : " (active)";
        return Results.NotFound(new { error = $"No workflow definition found for key '{key}'{versionSuffix}." });
    }

    var secretNames = await db.WorkflowSecrets
        .OrderBy(s => s.Name)
        .Select(s => s.Name)
        .ToListAsync(ct);

    try
    {
        var response = AvailableFieldsService.GetAvailableFields(definition, stepKey, secretNames);
        return Results.Ok(response);
    }
    catch (ArgumentException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
});

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

ops.MapPost("/manual-triggers/start", async (
    StartManualWorkflowRequest request,
    ClaimsPrincipal user,
    ManualWorkflowTriggerService service,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.WorkflowKey))
        return Results.BadRequest(new { error = "WorkflowKey is required." });

    if (request.TenantId == Guid.Empty)
        return Results.BadRequest(new { error = "TenantId is required." });

    if (string.IsNullOrWhiteSpace(request.ActorId))
    {
        request.ActorId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.Identity?.Name;
    }

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
    catch (WorkflowDefinitionNotActiveException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
    catch (WorkflowTriggerMismatchException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
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
    catch (WorkflowDefinitionNotActiveException ex)
    {
        return Results.Conflict(new { error = ex.Message });
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

// ── Helpers ───────────────────────────────────────────────────────────────────

static Dictionary<string, string> CaptureRequestHeaders(HttpRequest request)
{
    // These headers are transport noise or likely to carry secrets and should not be
    // persisted into trigger_data.
    var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Cookie", "Set-Cookie", "X-Api-Key",
        "Connection", "Content-Length", "Host", "Transfer-Encoding",
        "Accept-Encoding", "Accept-Language", "Upgrade-Insecure-Requests"
    };

    return request.Headers
        .Where(h => !excluded.Contains(h.Key))
        .ToDictionary(
            h => h.Key.ToLowerInvariant(),
            h => h.Value.ToString());
}
