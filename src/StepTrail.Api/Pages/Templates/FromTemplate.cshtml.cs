using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StepTrail.Api.UI;

namespace StepTrail.Api.Pages.Templates;

public sealed class FromTemplateModel : PageModel
{
    private readonly WorkflowApiClient _api;

    public FromTemplateModel(WorkflowApiClient api) => _api = api;

    [BindProperty(SupportsGet = true)]
    public string DescriptorKey { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public int DescriptorVersion { get; set; }

    public WorkflowDescriptorSummary? Template { get; private set; }
    public string? LoadError { get; private set; }

    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string Key { get; set; } = string.Empty;
    [BindProperty] public string TriggerType { get; set; } = "Webhook";

    public static readonly string[] TriggerTypes = ["Webhook", "Manual", "Api", "Schedule"];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(DescriptorKey))
            return RedirectToPage("/Templates/Catalog");

        Template = await LoadTemplateAsync(ct);
        if (Template is null)
        {
            LoadError = $"Template '{DescriptorKey}' was not found.";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            Name = Template.Name;
            Key = Template.Key + "-workflow";
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

        var result = await _api.CreateFromDescriptorAsync(
            DescriptorKey, DescriptorVersion, Name, Key, TriggerType, ct);

        if (result.Success)
            return Redirect($"/ops/definitions/edit?id={result.DefinitionId}");

        ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Failed to create workflow.");
        Template = await LoadTemplateAsync(ct);
        return Page();
    }

    private async Task<WorkflowDescriptorSummary?> LoadTemplateAsync(CancellationToken ct)
    {
        var templates = await _api.ListWorkflowsAsync(ct);
        return templates.FirstOrDefault(t =>
            t.Key == DescriptorKey &&
            (DescriptorVersion == 0 || t.Version == DescriptorVersion));
    }
}
