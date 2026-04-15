using System.Text.Json;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Runtime.OutputModels;
using StepTrail.Shared.Workflows;

namespace StepTrail.Worker.Handlers;

public sealed class DelayStepExecutor : IStepExecutor
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);

    public Task<StepExecutionResult> ExecuteAsync(StepExecutionRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.CurrentOutput))
            return Task.FromResult(StepExecutionResult.Success(request.CurrentOutput));

        if (string.IsNullOrWhiteSpace(request.StepConfiguration))
        {
            return Task.FromResult(
                StepExecutionResult.InvalidConfiguration(
                    $"Step '{request.StepKey}' uses DelayStepExecutor but has no configuration."));
        }

        DelayStepConfigurationSnapshot? configuration;
        try
        {
            configuration = JsonSerializer.Deserialize<DelayStepConfigurationSnapshot>(
                request.StepConfiguration,
                JsonSerializerOptions);
        }
        catch (JsonException ex)
        {
            return Task.FromResult(
                StepExecutionResult.InvalidConfiguration(
                    $"Step '{request.StepKey}': failed to deserialize DelayStepConfiguration.",
                    details: ex.Message));
        }

        if (configuration is null)
        {
            return Task.FromResult(
                StepExecutionResult.InvalidConfiguration(
                    $"Step '{request.StepKey}': failed to deserialize DelayStepConfiguration."));
        }

        if (configuration.DelaySeconds.HasValue && !string.IsNullOrWhiteSpace(configuration.TargetTimeExpression))
        {
            return Task.FromResult(
                StepExecutionResult.InvalidConfiguration(
                    $"Step '{request.StepKey}': delay configuration must define either delaySeconds or targetTimeExpression, but not both."));
        }

        if (configuration.DelaySeconds.HasValue)
        {
            if (configuration.DelaySeconds.Value < 1)
            {
                return Task.FromResult(
                    StepExecutionResult.InvalidConfiguration(
                        $"Step '{request.StepKey}': delay duration must be 1 second or greater."));
            }

            var resumeAtUtc = DateTimeOffset.UtcNow.AddSeconds(configuration.DelaySeconds.Value);
            var output = JsonSerializer.Serialize(
                new DelayStepOutput
                {
                    DelayType = "fixed",
                    RequestedDuration = TimeSpan.FromSeconds(configuration.DelaySeconds.Value).ToString("c"),
                    ResumeAtUtc = resumeAtUtc
                },
                JsonSerializerOptions);

            return Task.FromResult(StepExecutionResult.WaitUntil(resumeAtUtc, output));
        }

        if (string.IsNullOrWhiteSpace(configuration.TargetTimeExpression))
        {
            return Task.FromResult(
                StepExecutionResult.InvalidConfiguration(
                    $"Step '{request.StepKey}': delay configuration must define either delaySeconds or targetTimeExpression."));
        }

        return Task.FromResult(ExecuteDelayUntil(request, configuration.TargetTimeExpression));
    }

    private static StepExecutionResult ExecuteDelayUntil(
        StepExecutionRequest request,
        string targetTimeExpression)
    {
        var resolution = ResolveTargetTimeExpression(request, targetTimeExpression);
        if (!resolution.IsSuccess)
        {
            return resolution.FailureClassification == StepExecutionFailureClassification.InvalidConfiguration
                ? StepExecutionResult.InvalidConfiguration(resolution.Error!)
                : StepExecutionResult.InputResolutionFailure(resolution.Error!);
        }

        var targetTimeUtc = resolution.Value!.Value;
        var now = DateTimeOffset.UtcNow;
        var wasImmediate = targetTimeUtc <= now;
        var output = JsonSerializer.Serialize(
            new DelayStepOutput
            {
                DelayType = "until",
                TargetTimeUtc = targetTimeUtc,
                ResumeAtUtc = wasImmediate ? null : targetTimeUtc,
                WasImmediate = wasImmediate
            },
            JsonSerializerOptions);

        return wasImmediate
            ? StepExecutionResult.Success(output)
            : StepExecutionResult.WaitUntil(targetTimeUtc, output);
    }

    private static TargetTimeResolutionResult ResolveTargetTimeExpression(
        StepExecutionRequest request,
        string targetTimeExpression)
    {
        var expression = targetTimeExpression.Trim();
        var isDynamicReference = expression.StartsWith("{{", StringComparison.Ordinal)
            || expression.StartsWith("$", StringComparison.Ordinal);

        if (!isDynamicReference)
        {
            if (!DelayTimestampParser.TryParseUtcTimestamp(expression, out var literalTargetTimeUtc))
            {
                return TargetTimeResolutionResult.InvalidConfiguration(
                    $"Step '{request.StepKey}': delay-until target time '{expression}' is not a valid UTC timestamp.");
            }

            return TargetTimeResolutionResult.Success(literalTargetTimeUtc);
        }

        var resolvedValue = request.ResolveValueReference(
            expression,
            $"delay-until target time '{expression}'");

        if (!resolvedValue.IsSuccess)
        {
            return resolvedValue.FailureClassification == StepExecutionFailureClassification.InvalidConfiguration
                ? TargetTimeResolutionResult.InvalidConfiguration(resolvedValue.Error!)
                : TargetTimeResolutionResult.InputResolutionFailure(resolvedValue.Error!);
        }

        if (resolvedValue.Value is null)
        {
            return TargetTimeResolutionResult.InputResolutionFailure(
                $"Step '{request.StepKey}': delay-until target time '{expression}' resolved to null.");
        }

        if (resolvedValue.Value.Value.ValueKind != JsonValueKind.String)
        {
            return TargetTimeResolutionResult.InputResolutionFailure(
                $"Step '{request.StepKey}': delay-until target time '{expression}' must resolve to a string timestamp.");
        }

        var timestampText = resolvedValue.Value.Value.GetString();
        if (string.IsNullOrWhiteSpace(timestampText) || !DelayTimestampParser.TryParseUtcTimestamp(timestampText, out var targetTimeUtc))
        {
            return TargetTimeResolutionResult.InputResolutionFailure(
                $"Step '{request.StepKey}': delay-until target time '{expression}' resolved to '{timestampText}', which is not a valid UTC timestamp.");
        }

        return TargetTimeResolutionResult.Success(targetTimeUtc);
    }

    private sealed class DelayStepConfigurationSnapshot
    {
        public int? DelaySeconds { get; set; }
        public string? TargetTimeExpression { get; set; }
    }

    private sealed record TargetTimeResolutionResult(
        bool IsSuccess,
        DateTimeOffset? Value,
        string? Error,
        StepExecutionFailureClassification? FailureClassification)
    {
        public static TargetTimeResolutionResult Success(DateTimeOffset value) =>
            new(true, value, null, null);

        public static TargetTimeResolutionResult InvalidConfiguration(string error) =>
            new(false, null, error, StepExecutionFailureClassification.InvalidConfiguration);

        public static TargetTimeResolutionResult InputResolutionFailure(string error) =>
            new(false, null, error, StepExecutionFailureClassification.InputResolutionFailure);
    }
}
