using Microsoft.AspNetCore.Mvc.RazorPages;
using StepTrail.Api.Models;
using StepTrail.Api.UI;

namespace StepTrail.Api.Pages.Templates;

public sealed class IndexModel : PageModel
{
    private readonly WorkflowApiClient _api;

    public IndexModel(WorkflowApiClient api) => _api = api;

    public IReadOnlyList<WorkflowDefinitionSummary> Definitions { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        try
        {
            Definitions = await _api.ListDefinitionsAsync(ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not load workflow definitions: {ex.Message}";
        }
    }
}
