using System.Text.Json.Serialization;
using System.Text.Json;

namespace StepTrail.Shared.Runtime.OutputModels;

/// <summary>
/// Stable output contract for HttpRequest steps.
///
/// Persisted as JSON under the step execution's Output column and available
/// to subsequent steps via placeholder references:
///
///   {{steps.&lt;step_name&gt;.output.statusCode}}  — HTTP status code (integer)
///   {{steps.&lt;step_name&gt;.output.body.id}}     — parsed JSON response field when the body is JSON
///   {{steps.&lt;step_name&gt;.output.bodyText}}    — raw response body text
///   {{steps.&lt;step_name&gt;.output.headers}}     — response headers object (optional)
///
/// Both successful and failed HTTP responses produce this shape so that
/// subsequent steps can always inspect what the remote endpoint returned,
/// even when the step is retried or permanently failed.
/// </summary>
public sealed class HttpRequestStepOutput
{
    /// <summary>HTTP status code returned by the remote endpoint (e.g. 200, 404, 500). Uses 0 when no response was received.</summary>
    [JsonPropertyName("statusCode")]
    public required int StatusCode { get; init; }

    /// <summary>
    /// Response body as a JSON value.
    /// When the response content is JSON and parses successfully, this contains the parsed JSON object/array/scalar.
    /// Otherwise it contains the raw response body wrapped as a JSON string value.
    /// </summary>
    [JsonPropertyName("body")]
    public required JsonElement Body { get; init; }

    /// <summary>Raw response body text exactly as received. Empty string when the response had no body.</summary>
    [JsonPropertyName("bodyText")]
    public required string BodyText { get; init; }

    /// <summary>Response content type when provided by the remote endpoint.</summary>
    [JsonPropertyName("contentType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContentType { get; init; }

    /// <summary>
    /// Response headers keyed by lower-cased header name.
    /// Null when the caller chose not to capture headers (default).
    /// When present, multi-value headers are joined with ", " per RFC 7230.
    /// </summary>
    [JsonPropertyName("headers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>HTTP method used for the outbound request.</summary>
    [JsonPropertyName("requestMethod")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestMethod { get; init; }

    /// <summary>Resolved outbound request URL.</summary>
    [JsonPropertyName("requestUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestUrl { get; init; }
}
