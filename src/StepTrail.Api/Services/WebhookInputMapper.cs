using System.Text.Json;
using System.Text.Json.Nodes;
using StepTrail.Shared.Definitions;

namespace StepTrail.Api.Services;

/// <summary>
/// Applies the small first-version webhook input mapping model.
/// Source paths start with body, headers, or query and target paths define the
/// normalized workflow input object persisted for downstream execution.
/// </summary>
public sealed class WebhookInputMapper
{
    public object MapOrPassThrough(
        JsonElement payload,
        IReadOnlyDictionary<string, string>? headers,
        IReadOnlyDictionary<string, string>? query,
        IReadOnlyList<WebhookInputMapping> inputMappings)
    {
        ArgumentNullException.ThrowIfNull(inputMappings);

        if (inputMappings.Count == 0)
            return payload;

        var output = new JsonObject();
        var headerLookup = CreateLookup(headers);
        var queryLookup = CreateLookup(query);

        foreach (var mapping in inputMappings)
        {
            var valueNode = ResolveSourceValue(payload, headerLookup, queryLookup, mapping.SourcePath);
            ApplyTargetValue(output, mapping.TargetPath, valueNode);
        }

        return output;
    }

    private static Dictionary<string, string> CreateLookup(IReadOnlyDictionary<string, string>? values)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (values is null)
            return lookup;

        foreach (var pair in values)
            lookup[pair.Key] = pair.Value;

        return lookup;
    }

    private static JsonNode ResolveSourceValue(
        JsonElement payload,
        IReadOnlyDictionary<string, string> headers,
        IReadOnlyDictionary<string, string> query,
        string sourcePath)
    {
        var segments = SplitPath(sourcePath);
        var root = segments[0];

        if (string.Equals(root, "body", StringComparison.OrdinalIgnoreCase))
            return ResolveBodyValue(payload, segments[1..], sourcePath);

        if (string.Equals(root, "headers", StringComparison.OrdinalIgnoreCase))
            return ResolveDictionaryValue(headers, segments[1..], "headers", sourcePath);

        if (string.Equals(root, "query", StringComparison.OrdinalIgnoreCase))
            return ResolveDictionaryValue(query, segments[1..], "query", sourcePath);

        throw new WebhookTriggerInputMappingException(
            $"Webhook input mapping source path '{sourcePath}' must start with 'body', 'headers', or 'query'.");
    }

    private static JsonNode ResolveBodyValue(JsonElement payload, string[] path, string sourcePath)
    {
        var current = payload;

        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object)
            {
                throw new WebhookTriggerInputMappingException(
                    $"Webhook input mapping source path '{sourcePath}' cannot navigate into '{segment}' because the current value is {current.ValueKind}.");
            }

            if (!current.TryGetProperty(segment, out current))
            {
                throw new WebhookTriggerInputMappingException(
                    $"Webhook input mapping source path '{sourcePath}' could not find field '{segment}'.");
            }
        }

        return JsonNode.Parse(current.GetRawText())!;
    }

    private static JsonNode ResolveDictionaryValue(
        IReadOnlyDictionary<string, string> values,
        string[] path,
        string root,
        string sourcePath)
    {
        if (path.Length == 0)
        {
            throw new WebhookTriggerInputMappingException(
                $"Webhook input mapping source path '{sourcePath}' must include a key after '{root}'.");
        }

        var key = string.Join('.', path);
        if (!values.TryGetValue(key, out var value))
        {
            throw new WebhookTriggerInputMappingException(
                $"Webhook input mapping source path '{sourcePath}' could not find key '{key}'.");
        }

        return JsonValue.Create(value)!;
    }

    private static void ApplyTargetValue(JsonObject output, string targetPath, JsonNode value)
    {
        var segments = SplitPath(targetPath);
        JsonObject current = output;

        for (var index = 0; index < segments.Length - 1; index++)
        {
            var segment = segments[index];

            if (current[segment] is null)
            {
                var child = new JsonObject();
                current[segment] = child;
                current = child;
                continue;
            }

            if (current[segment] is JsonObject childObject)
            {
                current = childObject;
                continue;
            }

            throw new WebhookTriggerInputMappingException(
                $"Webhook input mapping target path '{targetPath}' conflicts with an existing scalar value at '{segment}'.");
        }

        current[segments[^1]] = value.DeepClone();
    }

    private static string[] SplitPath(string path)
    {
        var segments = path
            .Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            throw new WebhookTriggerInputMappingException(
                $"Webhook input mapping path '{path}' must not be empty.");
        }

        return segments;
    }
}

public sealed class WebhookTriggerInputMappingException : Exception
{
    public WebhookTriggerInputMappingException(string message) : base(message) { }
}
