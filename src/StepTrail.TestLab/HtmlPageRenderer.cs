using System.Net;
using System.Text;
using Microsoft.Extensions.Options;

namespace StepTrail.TestLab;

public sealed class HtmlPageRenderer
{
    private readonly IOptions<TestLabOptions> _options;

    public HtmlPageRenderer(IOptions<TestLabOptions> options)
    {
        _options = options;
    }

    public string Render(LabSnapshot snapshot, string? flashMessage = null)
    {
        var stepTrailBaseUrl = _options.Value.StepTrailApiBaseUrl.TrimEnd('/');
        var sb = new StringBuilder();

        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine("<title>StepTrail TestLab</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:Segoe UI,system-ui,sans-serif;background:#f4f6f8;color:#122230;margin:0;padding:24px;}");
        sb.AppendLine(".wrap{max-width:1100px;margin:0 auto;display:grid;gap:20px;}");
        sb.AppendLine(".card{background:#fff;border:1px solid #d9e1e7;border-radius:16px;padding:20px;box-shadow:0 8px 24px rgba(18,34,48,.05);}");
        sb.AppendLine("h1,h2{margin:0 0 10px;} h1{font-size:2rem;} h2{font-size:1.1rem;}");
        sb.AppendLine(".muted{color:#57707f;} .pill{display:inline-block;padding:6px 10px;border-radius:999px;background:#e8f0f5;font-size:.9rem;margin-right:8px;}");
        sb.AppendLine(".ok{background:#dff5e7;color:#14532d;} .warn{background:#fff4d6;color:#854d0e;} .bad{background:#fde4e4;color:#991b1b;}");
        sb.AppendLine(".actions{display:flex;flex-wrap:wrap;gap:10px;margin-top:14px;} form{margin:0;} button,a.btn{border:0;border-radius:999px;padding:10px 14px;background:#17384d;color:#fff;text-decoration:none;cursor:pointer;font:inherit;display:inline-block;}");
        sb.AppendLine("button.alt,a.btn.alt{background:#eff4f7;color:#17384d;} button.warn{background:#8a5a00;color:#fff;} button.bad{background:#9f2d2d;color:#fff;}");
        sb.AppendLine("table{width:100%;border-collapse:collapse;margin-top:12px;} th,td{padding:10px;border-top:1px solid #e6edf2;vertical-align:top;text-align:left;font-size:.92rem;}");
        sb.AppendLine("code,pre{font-family:Consolas,monospace;} pre{white-space:pre-wrap;background:#f7fafc;border-radius:12px;padding:10px;margin:0;max-width:100%;overflow:auto;}");
        sb.AppendLine("</style></head><body><div class=\"wrap\">");

        sb.AppendLine("<section class=\"card\">");
        sb.AppendLine("<h1>StepTrail TestLab</h1>");
        sb.AppendLine("<p class=\"muted\">A lightweight demo harness that keeps StepTrail.Api and StepTrail.Worker unchanged while giving you one repeatable workflow, mock downstream APIs, and fast scenario switching.</p>");
        if (!string.IsNullOrWhiteSpace(flashMessage))
        {
            sb.Append("<div class=\"pill ok\">");
            sb.Append(WebUtility.HtmlEncode(flashMessage));
            sb.AppendLine("</div>");
        }

        sb.Append("<div class=\"pill ");
        sb.Append(snapshot.DemoWorkflowReady ? "ok" : "warn");
        sb.Append("\">");
        sb.Append(WebUtility.HtmlEncode(snapshot.DemoWorkflowStatus ?? "Demo workflow not prepared yet."));
        sb.AppendLine("</div>");

        sb.Append("<div class=\"pill\">Active scenario: ");
        sb.Append(WebUtility.HtmlEncode(snapshot.ActiveScenario));
        sb.AppendLine("</div>");

        if (!string.IsNullOrWhiteSpace(snapshot.LastTriggerSummary))
        {
            sb.Append("<div class=\"pill warn\">");
            sb.Append(WebUtility.HtmlEncode(snapshot.LastTriggerSummary));
            sb.AppendLine("</div>");
        }

        sb.AppendLine("<div class=\"actions\">");
        sb.AppendLine("<form method=\"post\" action=\"/lab/setup\"><button type=\"submit\">Setup Demo Workflow</button></form>");
        sb.AppendLine("<form method=\"post\" action=\"/lab/reset\"><button type=\"submit\" class=\"alt\">Reset Lab Activity</button></form>");
        sb.AppendLine("<a class=\"btn alt\" href=\"" + WebUtility.HtmlEncode($"{stepTrailBaseUrl}/ops/workflows") + "\">Open Workflow Instances</a>");
        sb.AppendLine("<a class=\"btn alt\" href=\"" + WebUtility.HtmlEncode($"{stepTrailBaseUrl}/ops/definitions") + "\">Open Workflow Definitions</a>");
        if (snapshot.LastWorkflowInstanceId.HasValue)
        {
            sb.AppendLine("<a class=\"btn\" href=\"" + WebUtility.HtmlEncode($"{stepTrailBaseUrl}/ops/workflows/details?id={snapshot.LastWorkflowInstanceId}") + "\">Open Last Instance</a>");
        }
        sb.AppendLine("</div>");
        sb.AppendLine("</section>");

        sb.AppendLine("<section class=\"card\">");
        sb.AppendLine("<h2>Demo Scenarios</h2>");
        sb.AppendLine("<p class=\"muted\">Each trigger both activates the scenario and starts a new workflow instance through StepTrail's public webhook.</p>");
        sb.AppendLine("<div class=\"actions\">");
        sb.AppendLine("<form method=\"post\" action=\"/lab/trigger/" + LabScenarioNames.HappyPath + "\"><button type=\"submit\">Trigger Happy Path</button></form>");
        sb.AppendLine("<form method=\"post\" action=\"/lab/trigger/" + LabScenarioNames.FailThenRecover + "\"><button type=\"submit\" class=\"warn\">Trigger Fail Then Recover</button></form>");
        sb.AppendLine("<form method=\"post\" action=\"/lab/trigger/" + LabScenarioNames.PermanentFailure + "\"><button type=\"submit\" class=\"bad\">Trigger Permanent Failure</button></form>");
        sb.AppendLine("</div>");
        sb.AppendLine("<table><thead><tr><th>Metric</th><th>Value</th></tr></thead><tbody>");
        sb.AppendLine("<tr><td>API A calls</td><td>" + snapshot.ApiACallCount + "</td></tr>");
        sb.AppendLine("<tr><td>API B calls</td><td>" + snapshot.ApiBCallCount + "</td></tr>");
        sb.AppendLine("<tr><td>Webhook route</td><td><code>/webhooks/" + WebUtility.HtmlEncode(TestLabDefaults.WorkflowRouteKey) + "</code></td></tr>");
        sb.AppendLine("</tbody></table>");
        sb.AppendLine("</section>");

        sb.AppendLine("<section class=\"card\">");
        sb.AppendLine("<h2>Mock Traffic</h2>");
        sb.AppendLine("<p class=\"muted\">Recent downstream calls received by TestLab. This helps during demos when you want to prove that StepTrail really invoked external systems.</p>");

        if (snapshot.Requests.Count == 0)
        {
            sb.AppendLine("<p class=\"muted\">No mock traffic yet. Trigger one of the scenarios above.</p>");
        }
        else
        {
            sb.AppendLine("<table><thead><tr><th>UTC</th><th>Scenario</th><th>Endpoint</th><th>Status</th><th>Payload</th></tr></thead><tbody>");
            foreach (var record in snapshot.Requests)
            {
                sb.AppendLine("<tr>");
                sb.Append("<td>").Append(WebUtility.HtmlEncode(record.ReceivedAtUtc.ToString("yyyy-MM-dd HH:mm:ss"))).AppendLine("</td>");
                sb.Append("<td>").Append(WebUtility.HtmlEncode(record.Scenario)).AppendLine("</td>");
                sb.Append("<td><code>").Append(WebUtility.HtmlEncode(record.Method)).Append(' ').Append(WebUtility.HtmlEncode(record.Endpoint)).AppendLine("</code></td>");
                sb.Append("<td>").Append(record.ResponseStatusCode).AppendLine("</td>");
                sb.Append("<td><pre>").Append(WebUtility.HtmlEncode(record.Body)).AppendLine("</pre></td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        sb.AppendLine("</section>");

        sb.AppendLine("<section class=\"card\">");
        sb.AppendLine("<h2>Run Notes</h2>");
        sb.AppendLine("<p class=\"muted\">Suggested demo flow: start <code>StepTrail.Api</code>, start <code>StepTrail.Worker</code>, then run <code>StepTrail.TestLab</code> on <code>" + WebUtility.HtmlEncode(_options.Value.PublicBaseUrl) + "</code>. Use the fail-then-recover scenario first to show retries and the new Workflow Runs view.</p>");
        sb.AppendLine("</section>");

        sb.AppendLine("</div></body></html>");
        return sb.ToString();
    }
}
