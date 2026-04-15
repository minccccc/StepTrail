using System.Text.Json;
using StepTrail.Shared.Definitions;

namespace StepTrail.Api.Services;

/// <summary>
/// Extracts a webhook idempotency key from a configured body or header source path.
/// Missing or non-scalar values are treated as request failures rather than silently
/// disabling idempotency.
/// </summary>
public sealed class WebhookIdempotencyKeyExtractor
{
    public string? ExtractOrNone(
        JsonElement payload,
        IReadOnlyDictionary<string, string>? headers,
        WebhookIdempotencyKeyExtractionConfiguration? configuration)
    {
        if (configuration is null)
            return null;

        var segments = configuration.SourcePath
            .Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 2)
        {
            throw new WebhookTriggerIdempotencyExtractionException(
                $"Webhook idempotency source path '{configuration.SourcePath}' is invalid.");
        }

        var root = segments[0];

        if (string.Equals(root, "headers", StringComparison.OrdinalIgnoreCase))
        {
            var headerKey = string.Join('.', segments[1..]);
            var headerLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in headers ?? new Dictionary<string, string>())
                headerLookup[pair.Key] = pair.Value;

            if (!headerLookup.TryGetValue(headerKey, out var headerValue) || string.IsNullOrWhiteSpace(headerValue))
            {
                throw new WebhookTriggerIdempotencyExtractionException(
                    $"Webhook idempotency header '{headerKey}' was not found.");
            }

            return headerValue.Trim();
        }

        if (string.Equals(root, "body", StringComparison.OrdinalIgnoreCase))
        {
            var current = payload;
            foreach (var segment in segments[1..])
            {
                if (current.ValueKind != JsonValueKind.Object)
                {
                    throw new WebhookTriggerIdempotencyExtractionException(
                        $"Webhook idempotency body path '{configuration.SourcePath}' cannot navigate into '{segment}' because the current value is {current.ValueKind}.");
                }

                if (!current.TryGetProperty(segment, out current))
                {
                    throw new WebhookTriggerIdempotencyExtractionException(
                        $"Webhook idempotency body path '{configuration.SourcePath}' could not find field '{segment}'.");
                }
            }

            var extractedValue = current.ValueKind switch
            {
                JsonValueKind.String => current.GetString(),
                JsonValueKind.Number => current.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => throw new WebhookTriggerIdempotencyExtractionException(
                    $"Webhook idempotency body path '{configuration.SourcePath}' resolved to {current.ValueKind}. Only scalar values are supported.")
            };

            if (string.IsNullOrWhiteSpace(extractedValue))
            {
                throw new WebhookTriggerIdempotencyExtractionException(
                    $"Webhook idempotency body path '{configuration.SourcePath}' resolved to an empty value.");
            }

            return extractedValue.Trim();
        }

        throw new WebhookTriggerIdempotencyExtractionException(
            $"Webhook idempotency source path '{configuration.SourcePath}' must start with 'body' or 'headers'.");
    }
}

public sealed class WebhookTriggerIdempotencyExtractionException : Exception
{
    public WebhookTriggerIdempotencyExtractionException(string message) : base(message) { }
}
