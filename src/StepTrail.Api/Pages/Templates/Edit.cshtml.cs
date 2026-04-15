using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StepTrail.Api.Models;
using StepTrail.Api.UI;

namespace StepTrail.Api.Pages.Templates;

public sealed class EditModel : PageModel
{
    private readonly WorkflowApiClient _api;

    public EditModel(WorkflowApiClient api) => _api = api;

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public WorkflowDefinitionDetail? Definition { get; private set; }
    public string? LoadError { get; private set; }

    // Webhook trigger fields
    [BindProperty] public string? RouteKey { get; set; }
    [BindProperty] public string? WebhookHttpMethod { get; set; }
    [BindProperty] public string? SignatureHeaderName { get; set; }
    [BindProperty] public string? SignatureSecretName { get; set; }
    [BindProperty] public string? SignatureAlgorithm { get; set; }
    [BindProperty] public string? SignaturePrefix { get; set; }
    [BindProperty] public string? IdempotencyKeySourcePath { get; set; }

    // Manual trigger fields
    [BindProperty] public string? EntryPointKey { get; set; }

    // Api trigger fields
    [BindProperty] public string? OperationKey { get; set; }

    // Schedule trigger fields
    [BindProperty] public int? IntervalSeconds { get; set; }
    [BindProperty] public string? CronExpression { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Id == Guid.Empty)
            return RedirectToPage("/Templates/Index");

        Definition = await _api.GetDefinitionAsync(Id, ct);
        if (Definition is null)
        {
            LoadError = $"Workflow definition '{Id}' was not found.";
            return Page();
        }

        PopulateFromDefinition();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveTriggerAsync(CancellationToken ct)
    {
        Definition = await _api.GetDefinitionAsync(Id, ct);
        if (Definition is null)
        {
            LoadError = $"Workflow definition '{Id}' was not found.";
            return Page();
        }

        var request = new UpdateTriggerRequest
        {
            RouteKey = RouteKey,
            HttpMethod = WebhookHttpMethod,
            SignatureHeaderName = SignatureHeaderName,
            SignatureSecretName = SignatureSecretName,
            SignatureAlgorithm = SignatureAlgorithm,
            SignaturePrefix = SignaturePrefix,
            IdempotencyKeySourcePath = IdempotencyKeySourcePath,
            EntryPointKey = EntryPointKey,
            OperationKey = OperationKey,
            IntervalSeconds = IntervalSeconds,
            CronExpression = CronExpression
        };

        var result = await _api.UpdateTriggerAsync(Id, request, ct);

        if (result.Success)
        {
            TempData["SuccessMessage"] = "Trigger configuration saved.";
            return RedirectToPage(new { id = Id });
        }

        TempData["ErrorMessage"] = result.ErrorMessage;
        return RedirectToPage(new { id = Id });
    }

    // Step config save handler — stepKey is passed via form hidden field
    [BindProperty] public string? EditingStepKey { get; set; }
    [BindProperty] public string? StepUrl { get; set; }
    [BindProperty] public string? StepMethod { get; set; }
    [BindProperty] public string? StepHeaders { get; set; }
    [BindProperty] public string? StepBody { get; set; }
    [BindProperty] public int? StepTimeoutSeconds { get; set; }
    [BindProperty] public string? StepMappings { get; set; }
    [BindProperty] public string? StepSourcePath { get; set; }
    [BindProperty] public string? StepOperator { get; set; }
    [BindProperty] public string? StepExpectedValue { get; set; }
    [BindProperty] public string? StepFalseOutcome { get; set; }
    [BindProperty] public int? StepDelaySeconds { get; set; }
    [BindProperty] public string? StepTargetTimeExpression { get; set; }

    // Retry policy fields
    [BindProperty] public bool StepEnableRetryPolicy { get; set; }
    [BindProperty] public int? StepRetryMaxAttempts { get; set; }
    [BindProperty] public int? StepRetryInitialDelaySeconds { get; set; }
    [BindProperty] public string? StepRetryBackoffStrategy { get; set; }
    [BindProperty] public int? StepRetryMaxDelaySeconds { get; set; }
    [BindProperty] public bool StepRetryOnTimeout { get; set; } = true;

    public async Task<IActionResult> OnPostSaveStepAsync(CancellationToken ct)
    {
        Definition = await _api.GetDefinitionAsync(Id, ct);
        if (Definition is null || string.IsNullOrWhiteSpace(EditingStepKey))
        {
            LoadError = "Definition or step not found.";
            return Page();
        }

        var request = new UpdateStepRequest
        {
            Url = StepUrl,
            Method = StepMethod,
            Headers = StepHeaders,
            Body = StepBody,
            TimeoutSeconds = StepTimeoutSeconds,
            Mappings = StepMappings,
            SourcePath = StepSourcePath,
            Operator = StepOperator,
            ExpectedValue = StepExpectedValue,
            FalseOutcome = StepFalseOutcome,
            DelaySeconds = StepDelaySeconds,
            TargetTimeExpression = StepTargetTimeExpression,
            EnableRetryPolicy = StepEnableRetryPolicy,
            RetryMaxAttempts = StepRetryMaxAttempts,
            RetryInitialDelaySeconds = StepRetryInitialDelaySeconds,
            RetryBackoffStrategy = StepRetryBackoffStrategy,
            RetryMaxDelaySeconds = StepRetryMaxDelaySeconds,
            RetryOnTimeout = StepRetryOnTimeout
        };

        var result = await _api.UpdateStepAsync(Id, EditingStepKey, request, ct);

        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] =
            result.Success ? $"Step '{EditingStepKey}' configuration saved." : result.ErrorMessage;
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostActivateAsync(CancellationToken ct)
    {
        var result = await _api.ActivateDefinitionAsync(Id, ct);
        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] =
            result.Success ? "Workflow definition activated." : result.ErrorMessage;
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostDeactivateAsync(CancellationToken ct)
    {
        var result = await _api.DeactivateDefinitionAsync(Id, ct);
        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] =
            result.Success ? "Workflow definition deactivated." : result.ErrorMessage;
        return RedirectToPage(new { id = Id });
    }

    private void PopulateFromDefinition()
    {
        if (Definition?.Trigger is null) return;

        var t = Definition.Trigger;
        RouteKey = t.RouteKey;
        WebhookHttpMethod = t.HttpMethod;
        SignatureHeaderName = t.SignatureHeaderName;
        SignatureSecretName = t.SignatureSecretName;
        SignatureAlgorithm = t.SignatureAlgorithm;
        SignaturePrefix = t.SignaturePrefix;
        IdempotencyKeySourcePath = t.IdempotencyKeySourcePath;
        EntryPointKey = t.EntryPointKey;
        OperationKey = t.OperationKey;
        IntervalSeconds = t.IntervalSeconds;
        CronExpression = t.CronExpression;
    }
}
