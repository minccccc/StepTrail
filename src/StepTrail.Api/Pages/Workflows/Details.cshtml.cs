using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StepTrail.Api.Models;
using StepTrail.Api.UI;

namespace StepTrail.Api.Pages.Workflows;

public sealed class DetailsModel : PageModel
{
    private readonly WorkflowApiClient _api;

    public DetailsModel(WorkflowApiClient api) => _api = api;

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public WorkflowInstanceDetail? Instance { get; private set; }
    public WorkflowTrail? Trail { get; private set; }
    public string? LoadError { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Id == Guid.Empty)
            return RedirectToPage("/Workflows/Index", new { });

        Instance = await _api.GetInstanceAsync(Id, ct);

        if (Instance is null)
        {
            LoadError = $"Workflow instance '{Id}' was not found.";
            return Page();
        }

        Trail = await _api.GetTrailAsync(Id, ct);

        return Page();
    }

    public async Task<IActionResult> OnPostRetryAsync(CancellationToken ct)
    {
        var result = await _api.RetryAsync(Id, ct);
        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] =
            result.Success ? "Retry scheduled successfully." : result.ErrorMessage;
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostReplayAsync(CancellationToken ct)
    {
        var result = await _api.ReplayAsync(Id, ct);
        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] =
            result.Success ? "Replay scheduled from step 1." : result.ErrorMessage;
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostCancelAsync(CancellationToken ct)
    {
        var result = await _api.CancelAsync(Id, ct);
        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] =
            result.Success ? "Workflow instance cancelled." : result.ErrorMessage;
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostArchiveAsync(CancellationToken ct)
    {
        var result = await _api.ArchiveAsync(Id, ct);
        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] =
            result.Success ? "Workflow instance archived." : result.ErrorMessage;
        return RedirectToPage(new { id = Id });
    }
}
