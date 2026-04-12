using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StepTrail.Api.Models;
using StepTrail.Api.UI;

namespace StepTrail.Api.Pages.Workflows;

public sealed class IndexModel : PageModel
{
    private readonly WorkflowApiClient _api;

    public IndexModel(WorkflowApiClient api) => _api = api;

    public PagedResult<WorkflowInstanceSummary> Result { get; private set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool ShowArchived { get; set; }

    public string? ErrorMessage { get; private set; }

    public static readonly string[] KnownStatuses =
        ["Pending", "Running", "Completed", "Failed", "Cancelled", "Archived"];

    public async Task OnGetAsync(CancellationToken ct)
    {
        try
        {
            Result = await _api.ListInstancesAsync(
                status: StatusFilter,
                includeArchived: ShowArchived,
                ct: ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not load workflow instances: {ex.Message}";
        }
    }
}
