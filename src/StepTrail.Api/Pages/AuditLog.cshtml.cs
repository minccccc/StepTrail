using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StepTrail.Api.UI;

namespace StepTrail.Api.Pages;

public sealed class AuditLogModel : PageModel
{
    private readonly WorkflowApiClient _api;

    public AuditLogModel(WorkflowApiClient api) => _api = api;

    [BindProperty(SupportsGet = true)]
    public int Days { get; set; } = 30;

    public AuditLogDashboard? Dashboard { get; private set; }
    public string? LoadError { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Days = Math.Clamp(Days, 1, 365);
        try
        {
            Dashboard = await _api.GetAuditLogAsync(Days, ct);
        }
        catch (Exception ex)
        {
            LoadError = $"Could not load audit log: {ex.Message}";
        }
    }
}
