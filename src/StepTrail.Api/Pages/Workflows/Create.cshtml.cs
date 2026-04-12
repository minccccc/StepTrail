using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StepTrail.Api.UI;

namespace StepTrail.Api.Pages.Workflows;

public sealed class CreateModel : PageModel
{
    private readonly WorkflowApiClient _api;

    public CreateModel(WorkflowApiClient api) => _api = api;

    public IReadOnlyList<WorkflowDescriptorSummary> Workflows { get; private set; } = [];
    public string? LoadError { get; private set; }

    [BindProperty] public string WorkflowKey { get; set; } = string.Empty;
    [BindProperty] public string? ExternalKey { get; set; }
    [BindProperty] public string? InputJson { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        try
        {
            Workflows = await _api.ListWorkflowsAsync(ct);
        }
        catch (Exception ex)
        {
            LoadError = $"Could not load workflows: {ex.Message}";
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(WorkflowKey))
        {
            ModelState.AddModelError(nameof(WorkflowKey), "Please select a workflow.");
            Workflows = await _api.ListWorkflowsAsync(ct);
            return Page();
        }

        var result = await _api.CreateInstanceAsync(WorkflowKey, ExternalKey, InputJson, ct);

        if (result.Success)
            return Redirect($"/ops/workflows/details?id={result.InstanceId}");

        ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Failed to create instance.");
        Workflows = await _api.ListWorkflowsAsync(ct);
        return Page();
    }
}
