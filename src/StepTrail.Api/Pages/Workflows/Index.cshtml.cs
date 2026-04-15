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

    /// <summary>Available workflow keys for the filter dropdown.</summary>
    public IReadOnlyList<string> AvailableWorkflowKeys { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? WorkflowKey { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? TriggerType { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? CreatedFrom { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? CreatedTo { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool ShowArchived { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int CurrentPage { get; set; } = 1;

    public string? ErrorMessage { get; private set; }

    public static readonly string[] KnownStatuses =
        ["Pending", "Running", "AwaitingRetry", "Completed", "Failed", "Cancelled", "Archived"];

    public static readonly string[] KnownTriggerTypes =
        ["Webhook", "Manual", "Api", "Schedule"];

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(StatusFilter) ||
        !string.IsNullOrWhiteSpace(WorkflowKey) ||
        !string.IsNullOrWhiteSpace(TriggerType) ||
        !string.IsNullOrWhiteSpace(CreatedFrom) ||
        !string.IsNullOrWhiteSpace(CreatedTo) ||
        ShowArchived;

    public async Task OnGetAsync(CancellationToken ct)
    {
        try
        {
            var definitions = await _api.ListDefinitionsAsync(ct);
            AvailableWorkflowKeys = definitions
                .Select(d => d.Key)
                .Distinct()
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            DateTimeOffset? parsedFrom = null;
            DateTimeOffset? parsedTo = null;

            if (!string.IsNullOrWhiteSpace(CreatedFrom) &&
                DateTimeOffset.TryParse(CreatedFrom, out var from))
                parsedFrom = from;

            if (!string.IsNullOrWhiteSpace(CreatedTo) &&
                DateTimeOffset.TryParse(CreatedTo, out var to))
                parsedTo = to.Date.AddDays(1).AddTicks(-1);

            Result = await _api.ListInstancesAsync(
                status: StatusFilter,
                workflowKey: WorkflowKey,
                triggerType: TriggerType,
                createdFrom: parsedFrom,
                createdTo: parsedTo,
                includeArchived: ShowArchived,
                page: Math.Max(CurrentPage, 1),
                ct: ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not load workflow instances: {ex.Message}";
        }
    }
}
