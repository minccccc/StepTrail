using System.Text.Json.Serialization;

namespace StepTrail.Shared.Definitions;

/// <summary>
/// Optional override configuration for HTTP response classification.
/// When omitted, the runtime uses default rules:
/// 2xx = success, 408/429/5xx = retryable, everything else = permanent failure.
/// </summary>
public sealed class HttpResponseClassificationConfiguration
{
    [JsonConstructor]
    public HttpResponseClassificationConfiguration()
    {
        SuccessStatusCodes = [];
        RetryableStatusCodes = [];
    }

    public HttpResponseClassificationConfiguration(
        IEnumerable<int>? successStatusCodes = null,
        IEnumerable<int>? retryableStatusCodes = null)
    {
        var normalizedSuccessStatusCodes = NormalizeStatusCodes(successStatusCodes, nameof(successStatusCodes));
        var normalizedRetryableStatusCodes = NormalizeStatusCodes(retryableStatusCodes, nameof(retryableStatusCodes));

        var overlappingStatusCodes = normalizedSuccessStatusCodes
            .Intersect(normalizedRetryableStatusCodes)
            .OrderBy(code => code)
            .ToList();

        if (overlappingStatusCodes.Count > 0)
        {
            throw new ArgumentException(
                $"HTTP response classification cannot mark the same status code as both success and retryable: {string.Join(", ", overlappingStatusCodes)}.",
                nameof(retryableStatusCodes));
        }

        SuccessStatusCodes = normalizedSuccessStatusCodes.ToList();
        RetryableStatusCodes = normalizedRetryableStatusCodes.ToList();
    }

    public List<int> SuccessStatusCodes { get; set; }
    public List<int> RetryableStatusCodes { get; set; }

    private static IReadOnlyList<int> NormalizeStatusCodes(IEnumerable<int>? statusCodes, string paramName)
    {
        if (statusCodes is null)
            return [];

        var normalizedStatusCodes = statusCodes
            .Distinct()
            .OrderBy(code => code)
            .ToList();

        foreach (var statusCode in normalizedStatusCodes)
        {
            if (statusCode < 100 || statusCode > 599)
            {
                throw new ArgumentOutOfRangeException(
                    paramName,
                    $"HTTP status code '{statusCode}' must be between 100 and 599.");
            }
        }

        return normalizedStatusCodes;
    }
}
