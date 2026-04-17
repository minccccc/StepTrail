using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using StepTrail.Api.Models;
using StepTrail.Api.Services;
using StepTrail.Shared;
using StepTrail.Shared.Entities;

namespace StepTrail.Api.Endpoints;

public static class PublicEndpoints
{
    public static WebApplication MapPublicEndpoints(this WebApplication app)
    {
        app.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        }).AllowAnonymous();

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

        return app;
    }

    private static Dictionary<string, string> CaptureRequestHeaders(HttpRequest request)
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
}
