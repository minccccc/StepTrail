using System.Text.Json.Serialization;

namespace StepTrail.Shared.Runtime.OutputModels;

/// <summary>
/// Stable output contract for HttpRequest and SendWebhook steps.
///
/// Persisted as JSON under the step execution's Output column and available
/// to subsequent steps via placeholder references:
///
///   {{steps.&lt;step_name&gt;.output.statusCode}}  — HTTP status code (integer)
///   {{steps.&lt;step_name&gt;.output.body}}        — raw response body string
///   {{steps.&lt;step_name&gt;.output.headers}}     — response headers object (optional)
///
/// Both successful and failed HTTP responses produce this shape so that
/// subsequent steps can always inspect what the remote endpoint returned,
/// even when the step is retried or permanently failed.
/// </summary>
public sealed class HttpRequestStepOutput
{
    /// <summary>HTTP status code returned by the remote endpoint (e.g. 200, 404, 500).</summary>
    [JsonPropertyName("statusCode")]
    public required int StatusCode { get; init; }

    /// <summary>Raw response body as a string. Empty string when the response had no body.</summary>
    [JsonPropertyName("body")]
    public required string Body { get; init; }

    /// <summary>
    /// Response headers keyed by lower-cased header name.
    /// Null when the caller chose not to capture headers (default).
    /// When present, multi-value headers are joined with ", " per RFC 7230.
    /// </summary>
    [JsonPropertyName("headers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
}
