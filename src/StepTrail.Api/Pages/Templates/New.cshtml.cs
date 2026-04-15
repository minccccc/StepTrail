using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StepTrail.Api.Models;
using StepTrail.Api.UI;

namespace StepTrail.Api.Pages.Templates;

public sealed class NewModel : PageModel
{
    private readonly WorkflowApiClient _api;

    public NewModel(WorkflowApiClient api) => _api = api;

    public IReadOnlyList<WorkflowDefinitionSummary> Templates { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    [BindProperty] public string BlankName { get; set; } = string.Empty;
    [BindProperty] public string BlankKey { get; set; } = string.Empty;
    [BindProperty] public string BlankTriggerType { get; set; } = "Webhook";

    public static readonly string[] TriggerTypes = ["Webhook", "Manual", "Api", "Schedule"];

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadTemplatesAsync(ct);
    }

    public async Task<IActionResult> OnPostCreateBlankAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(BlankName))
            ModelState.AddModelError(nameof(BlankName), "Name is required.");
        if (string.IsNullOrWhiteSpace(BlankKey))
            ModelState.AddModelError(nameof(BlankKey), "Key is required.");

        if (!ModelState.IsValid)
        {
            await LoadTemplatesAsync(ct);
            return Page();
        }

        var result = await _api.CreateBlankDefinitionAsync(BlankName, BlankKey, BlankTriggerType, ct);

        if (result.Success)
            return Redirect($"/ops/definitions/edit?id={result.DefinitionId}");

        ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Failed to create definition.");
        await LoadTemplatesAsync(ct);
        return Page();
    }

    private async Task LoadTemplatesAsync(CancellationToken ct)
    {
        try
        {
            var definitions = await _api.ListDefinitionsAsync(ct);
            Templates = definitions
                .Where(d => d.Status == "Active")
                .ToList();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not load templates: {ex.Message}";
        }
    }
}
