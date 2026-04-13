using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StepTrail.Api.Pages;

[AllowAnonymous]
public sealed class LoginModel : PageModel
{
    private readonly IConfiguration _config;

    public LoginModel(IConfiguration config) => _config = config;

    [BindProperty] public string Username { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    public string? ErrorMessage { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        var expectedUsername = _config.GetValue<string>("Ops:Username") ?? "admin";
        var expectedPassword = _config.GetValue<string>("Ops:Password") ?? "admin";

        if (Username != expectedUsername || Password != expectedPassword)
        {
            ErrorMessage = "Invalid username or password.";
            return Page();
        }

        var claims = new List<Claim> { new(ClaimTypes.Name, Username) };
        var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        // LocalRedirect guards against open-redirect attacks.
        return LocalRedirect(
            !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? returnUrl
                : "/ops/workflows");
    }
}
