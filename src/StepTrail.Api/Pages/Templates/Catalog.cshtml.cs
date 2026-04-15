using Microsoft.AspNetCore.Mvc.RazorPages;
using StepTrail.Api.UI;

namespace StepTrail.Api.Pages.Templates;

public sealed class CatalogModel : PageModel
{
    private readonly WorkflowApiClient _api;

    public CatalogModel(WorkflowApiClient api) => _api = api;

    public IReadOnlyList<WorkflowDescriptorSummary> Templates { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        try
        {
            Templates = await _api.ListWorkflowsAsync(ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not load templates: {ex.Message}";
        }
    }
}
