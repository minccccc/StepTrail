using Microsoft.AspNetCore.Mvc.RazorPages;
using StepTrail.Api.UI;

namespace StepTrail.Api.Pages;

public sealed class GettingStartedModel : PageModel
{
    private readonly WorkflowApiClient _api;

    public GettingStartedModel(WorkflowApiClient api) => _api = api;

    public int TemplateCount { get; private set; }
    public int WorkflowDefinitionCount { get; private set; }
    public int ActiveWorkflowCount { get; private set; }
    public int InstanceCount { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        try
        {
            var templates = await _api.ListWorkflowsAsync(ct);
            TemplateCount = templates.Count;
        }
        catch { TemplateCount = 0; }

        try
        {
            var definitions = await _api.ListDefinitionsAsync(ct);
            WorkflowDefinitionCount = definitions.Count;
            ActiveWorkflowCount = definitions.Count(d => d.Status == "Active");
        }
        catch { WorkflowDefinitionCount = 0; }

        try
        {
            var result = await _api.ListInstancesAsync(pageSize: 1, ct: ct);
            InstanceCount = result.Total;
        }
        catch { InstanceCount = 0; }
    }
}
