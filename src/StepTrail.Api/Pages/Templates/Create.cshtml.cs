using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StepTrail.Api.Models;
using StepTrail.Api.UI;

namespace StepTrail.Api.Pages.Templates;

public sealed class CreateModel : PageModel
{
    private readonly WorkflowApiClient _api;

    public CreateModel(WorkflowApiClient api) => _api = api;

    [BindProperty(SupportsGet = true)]
    public Guid TemplateId { get; set; }

    public WorkflowDefinitionSummary? Template { get; private set; }
    public string? LoadError { get; private set; }

    [BindProperty]
    public string Name { get; set; } = string.Empty;

    [BindProperty]
    public string Key { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (TemplateId == Guid.Empty)
            return RedirectToPage("/Templates/New");

        Template = await LoadTemplateAsync(ct);
        if (Template is null)
        {
            LoadError = $"Template '{TemplateId}' was not found.";
            return Page();
        }

        // Pre-populate name and key from template
        if (string.IsNullOrWhiteSpace(Name))
        {
            Name = $"{Template.Name} (copy)";
            Key = $"{Template.Key}-copy";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Name))
            ModelState.AddModelError(nameof(Name), "Name is required.");
        if (string.IsNullOrWhiteSpace(Key))
            ModelState.AddModelError(nameof(Key), "Key is required.");

        if (!ModelState.IsValid)
        {
            Template = await LoadTemplateAsync(ct);
            return Page();
        }

        var result = await _api.CloneDefinitionAsync(TemplateId, Name, Key, ct);

        if (result.Success)
            return Redirect($"/ops/definitions/edit?id={result.DefinitionId}");

        ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Failed to create definition.");
        Template = await LoadTemplateAsync(ct);
        return Page();
    }

    private async Task<WorkflowDefinitionSummary?> LoadTemplateAsync(CancellationToken ct)
    {
        var definitions = await _api.ListDefinitionsAsync(ct);
        return definitions.FirstOrDefault(d => d.Id == TemplateId);
    }
}
