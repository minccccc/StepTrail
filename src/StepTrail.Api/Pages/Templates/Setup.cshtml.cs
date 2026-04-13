using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StepTrail.Api.UI;

namespace StepTrail.Api.Pages.Templates;

public sealed class SetupModel : PageModel
{
    private const string WorkflowKey   = "webhook-to-http-call";
    private const string SecretName    = "webhook-to-http-call-url";

    private readonly WorkflowApiClient _api;

    public SetupModel(WorkflowApiClient api) => _api = api;

    [BindProperty] public string  TargetUrl  { get; set; } = string.Empty;
    [BindProperty] public string  HttpMethod { get; set; } = "POST";

    public string?  ErrorMessage { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(TargetUrl))
        {
            ModelState.AddModelError(nameof(TargetUrl), "Target URL is required.");
            return Page();
        }

        if (!Uri.TryCreate(TargetUrl.Trim(), UriKind.Absolute, out _))
        {
            ModelState.AddModelError(nameof(TargetUrl), "Target URL must be an absolute URL.");
            return Page();
        }

        // Persist the chosen URL as a named secret so the workflow can resolve it at runtime.
        var saveResult = await _api.UpsertSecretAsync(
            SecretName,
            TargetUrl.Trim(),
            description: "Target URL for the Webhook → HTTP Call template.",
            ct);

        if (!saveResult.Success)
        {
            ErrorMessage = $"Could not save configuration: {saveResult.ErrorMessage}";
            return Page();
        }

        // Update the step config if method differs from the default.
        // The workflow descriptor always stores POST; a method override would require a separate
        // secret or a descriptor change. For now just start the instance — the descriptor handles POST.

        // Start a workflow instance immediately so the user sees it running.
        var runResult = await _api.CreateInstanceAsync(WorkflowKey, externalKey: null, inputJson: null, ct);

        if (!runResult.Success)
        {
            ErrorMessage = $"Configuration saved, but could not start a workflow instance: {runResult.ErrorMessage}";
            return Page();
        }

        return Redirect($"/ops/workflows/details?id={runResult.InstanceId}");
    }
}
