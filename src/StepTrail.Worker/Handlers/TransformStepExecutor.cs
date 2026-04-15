using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Workflows;

namespace StepTrail.Worker.Handlers;

/// <summary>
/// Declarative transform/map executor.
/// Builds a new JSON object by resolving each configured source reference and writing it
/// to the configured target path in the output object.
/// </summary>
public sealed class TransformStepExecutor : IStepExecutor
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);

    public Task<StepExecutionResult> ExecuteAsync(StepExecutionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.StepConfiguration))
        {
            return Task.FromResult(
                StepExecutionResult.InvalidConfiguration(
                    $"Step '{request.StepKey}' uses TransformStepExecutor but has no configuration."));
        }

        TransformStepConfiguration? configuration;
        try
        {
            var snapshot = JsonSerializer.Deserialize<TransformStepConfigurationSnapshot>(
                request.StepConfiguration,
                JsonSerializerOptions);

            configuration = snapshot is null
                ? null
                : new TransformStepConfiguration(
                    snapshot.Mappings.Select(DeserializeMapping));
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return Task.FromResult(
                StepExecutionResult.InvalidConfiguration(
                    $"Step '{request.StepKey}': failed to deserialize TransformStepConfiguration.",
                    details: ex.Message));
        }

        if (configuration is null || configuration.Mappings.Count == 0)
        {
            return Task.FromResult(
                StepExecutionResult.InvalidConfiguration(
                    $"Step '{request.StepKey}': transform configuration must contain at least one mapping."));
        }

        var output = new JsonObject();

        foreach (var mapping in configuration.Mappings)
        {
            string normalizedTargetPath;
            try
            {
                normalizedTargetPath = TransformValueMapping.NormalizeTargetPath(mapping.TargetPath);
            }
            catch (ArgumentException ex)
            {
                return Task.FromResult(
                    StepExecutionResult.InvalidConfiguration(
                        $"Step '{request.StepKey}': transform target path '{mapping.TargetPath}' is invalid.",
                        details: ex.Message));
            }

            var resolvedSource = ResolveMappingValue(request, mapping);
            if (!resolvedSource.IsSuccess)
                return Task.FromResult(ToFailureResult(resolvedSource));

            if (!TryWriteValue(output, normalizedTargetPath, resolvedSource.Value!.Value, out var writeError))
            {
                return Task.FromResult(
                    StepExecutionResult.InvalidConfiguration(
                        $"Step '{request.StepKey}': transform target path '{mapping.TargetPath}' is invalid.",
                        details: writeError));
            }
        }

        return Task.FromResult(
            StepExecutionResult.Success(output.ToJsonString(JsonSerializerOptions)));
    }

    private static TransformValueMapping DeserializeMapping(TransformValueMappingSnapshot mapping)
    {
        if (mapping.Operation is not null)
            return new TransformValueMapping(mapping.TargetPath, DeserializeOperation(mapping.Operation));

        return new TransformValueMapping(mapping.TargetPath, mapping.SourcePath ?? string.Empty);
    }

    private static TransformValueOperation DeserializeOperation(TransformValueOperationSnapshot operation) =>
        operation.Type switch
        {
            TransformOperationType.DefaultValue => TransformValueOperation.CreateDefaultValue(
                operation.SourcePath ?? string.Empty,
                operation.DefaultValue ?? string.Empty),
            TransformOperationType.Concatenate => TransformValueOperation.CreateConcatenate(operation.Parts ?? []),
            TransformOperationType.FormatString => TransformValueOperation.CreateFormatString(
                operation.Template ?? string.Empty,
                operation.Arguments ?? []),
            _ => throw new ArgumentOutOfRangeException(nameof(operation.Type), operation.Type, "Unsupported transform operation type.")
        };

    private static ValueResolutionResult ResolveMappingValue(StepExecutionRequest request, TransformValueMapping mapping)
    {
        if (!string.IsNullOrWhiteSpace(mapping.SourcePath))
        {
            return request.ResolveValueReference(
                mapping.SourcePath,
                $"transform source '{mapping.SourcePath}'");
        }

        if (mapping.Operation is null)
        {
            return ValueResolutionResult.InvalidConfiguration(
                $"Step '{request.StepKey}': transform mapping for target '{mapping.TargetPath}' must define sourcePath or operation.");
        }

        return mapping.Operation.Type switch
        {
            TransformOperationType.DefaultValue => ResolveDefaultValueOperation(request, mapping),
            TransformOperationType.Concatenate => ResolveConcatenateOperation(request, mapping),
            TransformOperationType.FormatString => ResolveFormatStringOperation(request, mapping),
            _ => ValueResolutionResult.InvalidConfiguration(
                $"Step '{request.StepKey}': transform operation '{mapping.Operation.Type}' is not supported.")
        };
    }

    private static ValueResolutionResult ResolveDefaultValueOperation(
        StepExecutionRequest request,
        TransformValueMapping mapping)
    {
        var operation = mapping.Operation!;
        var resolvedValue = request.ResolveValueReference(
            operation.SourcePath,
            $"default value source '{operation.SourcePath}'");

        if (resolvedValue.IsSuccess && resolvedValue.Value!.Value.ValueKind != JsonValueKind.Null)
            return resolvedValue;

        if (!resolvedValue.IsSuccess
            && resolvedValue.FailureClassification == StepExecutionFailureClassification.InvalidConfiguration)
        {
            return resolvedValue;
        }

        return ValueResolutionResult.Success(JsonSerializer.SerializeToElement(operation.DefaultValue ?? string.Empty));
    }

    private static ValueResolutionResult ResolveConcatenateOperation(
        StepExecutionRequest request,
        TransformValueMapping mapping)
    {
        var parts = new List<string>();

        foreach (var part in mapping.Operation!.Parts)
        {
            var resolvedPart = ResolveStringOperand(
                request,
                part,
                $"concatenate part for target '{mapping.TargetPath}'");

            if (!resolvedPart.IsSuccess)
                return resolvedPart;

            parts.Add(resolvedPart.Value!.Value.GetString()!);
        }

        return ValueResolutionResult.Success(JsonSerializer.SerializeToElement(string.Concat(parts)));
    }

    private static ValueResolutionResult ResolveFormatStringOperation(
        StepExecutionRequest request,
        TransformValueMapping mapping)
    {
        var operation = mapping.Operation!;
        var arguments = new object[operation.Arguments.Count];

        for (var index = 0; index < operation.Arguments.Count; index++)
        {
            var resolvedArgument = ResolveStringOperand(
                request,
                operation.Arguments[index],
                $"format argument {index} for target '{mapping.TargetPath}'");

            if (!resolvedArgument.IsSuccess)
                return resolvedArgument;

            arguments[index] = resolvedArgument.Value!.Value.GetString()!;
        }

        try
        {
            var formatted = string.Format(CultureInfo.InvariantCulture, operation.Template!, arguments);
            return ValueResolutionResult.Success(JsonSerializer.SerializeToElement(formatted));
        }
        catch (FormatException ex)
        {
            return ValueResolutionResult.InvalidConfiguration(
                $"Step '{request.StepKey}': format string transform operation for target '{mapping.TargetPath}' is invalid - {ex.Message}");
        }
    }

    private static ValueResolutionResult ResolveStringOperand(
        StepExecutionRequest request,
        string operand,
        string fieldDescription)
    {
        if (operand.Contains("{{", StringComparison.Ordinal))
        {
            var resolvedTemplate = request.ResolveTemplate(operand, fieldDescription);
            return resolvedTemplate.IsSuccess
                ? ValueResolutionResult.Success(JsonSerializer.SerializeToElement(resolvedTemplate.Value!))
                : ValueResolutionResult.InputResolutionFailure(resolvedTemplate.Error!);
        }

        if (operand == "$" || operand.StartsWith("$.", StringComparison.Ordinal))
        {
            var resolvedValue = request.ResolveValueReference(operand, fieldDescription);
            if (!resolvedValue.IsSuccess)
                return resolvedValue;

            return TryConvertScalarToString(resolvedValue.Value!.Value, request.StepKey, fieldDescription);
        }

        return ValueResolutionResult.Success(JsonSerializer.SerializeToElement(operand));
    }

    private static ValueResolutionResult TryConvertScalarToString(
        JsonElement value,
        string stepKey,
        string fieldDescription)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => ValueResolutionResult.Success(JsonSerializer.SerializeToElement(value.GetString()!)),
            JsonValueKind.Number => ValueResolutionResult.Success(JsonSerializer.SerializeToElement(value.GetRawText())),
            JsonValueKind.True => ValueResolutionResult.Success(JsonSerializer.SerializeToElement("true")),
            JsonValueKind.False => ValueResolutionResult.Success(JsonSerializer.SerializeToElement("false")),
            JsonValueKind.Null => ValueResolutionResult.InputResolutionFailure(
                $"Step '{stepKey}': {fieldDescription} resolved to null, which is not allowed in string operations."),
            JsonValueKind.Object => ValueResolutionResult.InputResolutionFailure(
                $"Step '{stepKey}': {fieldDescription} resolved to an object, which is not allowed in string operations."),
            JsonValueKind.Array => ValueResolutionResult.InputResolutionFailure(
                $"Step '{stepKey}': {fieldDescription} resolved to an array, which is not allowed in string operations."),
            _ => ValueResolutionResult.InputResolutionFailure(
                $"Step '{stepKey}': {fieldDescription} resolved to unsupported JSON kind '{value.ValueKind}'.")
        };
    }

    private static StepExecutionResult ToFailureResult(ValueResolutionResult result) =>
        result.FailureClassification switch
        {
            StepExecutionFailureClassification.InvalidConfiguration =>
                StepExecutionResult.InvalidConfiguration(result.Error!),
            _ =>
                StepExecutionResult.InputResolutionFailure(result.Error!)
        };

    private static bool TryWriteValue(
        JsonObject output,
        string targetPath,
        JsonElement value,
        out string? error)
    {
        var segments = targetPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            error = "Target path must contain at least one segment.";
            return false;
        }

        JsonObject current = output;

        for (var index = 0; index < segments.Length - 1; index++)
        {
            var segment = segments[index];
            var existing = current[segment];

            if (existing is null)
            {
                var child = new JsonObject();
                current[segment] = child;
                current = child;
                continue;
            }

            if (existing is JsonObject existingObject)
            {
                current = existingObject;
                continue;
            }

            error = $"Target path '{targetPath}' conflicts with an existing scalar value at '{segment}'.";
            return false;
        }

        var leafSegment = segments[^1];
        if (current[leafSegment] is not null)
        {
            error = $"Target path '{targetPath}' is assigned more than once.";
            return false;
        }

        current[leafSegment] = JsonNode.Parse(value.GetRawText());
        error = null;
        return true;
    }

    private sealed class TransformStepConfigurationSnapshot
    {
        public List<TransformValueMappingSnapshot> Mappings { get; set; } = [];
    }

    private sealed class TransformValueMappingSnapshot
    {
        public string TargetPath { get; set; } = string.Empty;
        public string? SourcePath { get; set; }
        public TransformValueOperationSnapshot? Operation { get; set; }
    }

    private sealed class TransformValueOperationSnapshot
    {
        public TransformOperationType Type { get; set; }
        public string? SourcePath { get; set; }
        public string? DefaultValue { get; set; }
        public string? Template { get; set; }
        public List<string>? Parts { get; set; }
        public List<string>? Arguments { get; set; }
    }
}
