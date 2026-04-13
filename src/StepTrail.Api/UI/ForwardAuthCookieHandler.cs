namespace StepTrail.Api.UI;

/// <summary>
/// DelegatingHandler that copies the current HTTP request's Cookie header onto every
/// outbound call made by WorkflowApiClient. This allows the typed HTTP client to call
/// the same-process REST API while inheriting the authenticated user's session cookie,
/// so that .RequireAuthorization() on the API endpoints is satisfied for loopback calls.
/// </summary>
internal sealed class ForwardAuthCookieHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ForwardAuthCookieHandler(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct)
    {
        var cookieHeader = _httpContextAccessor.HttpContext?
            .Request.Headers.Cookie
            .FirstOrDefault();

        if (cookieHeader is not null)
            request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

        return base.SendAsync(request, ct);
    }
}
