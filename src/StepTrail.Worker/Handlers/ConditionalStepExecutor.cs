using System.Text.Json;
using System.Text.Json.Serialization;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Runtime.OutputModels;
using StepTrail.Shared.Workflows;

namespace StepTrail.Worker.Handlers;

public sealed class ConditionalStepExecutor : IStepExecutor
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = CreateJsonSerializerOptions();

    public Task<StepExecutionResult> ExecuteAsync(StepExecutionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.StepConfiguration))
        {
            return Task.FromResult(
                StepExecutionResult.InvalidConfiguration(
                    $"Step '{request.StepKey}' uses ConditionalStepExecutor but has no configuration."));
        }

        ConditionalStepConfiguration? configuration;
        try
        {
            var snapshot = JsonSerializer.Deserialize<ConditionalStepConfigurationSnapshot>(
                request.StepConfiguration,
                JsonSerializerOptions);

            configuration = snapshot is null
                ? null
                : DeserializeConfiguration(snapshot);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or ArgumentOutOfRangeException)
        {
            return Task.FromResult(
                StepExecutionResult.InvalidConfiguration(
                    $"Step '{request.StepKey}': failed to deserialize ConditionalStepConfiguration.",
                    details: ex.Message));
        }

        if (configuration is null)
        {
            return Task.FromResult(
                StepExecutionResult.InvalidConfiguration(
                    $"Step '{request.StepKey}': failed to deserialize ConditionalStepConfiguration."));
        }

        var evaluation = EvaluateCondition(request, configuration);
        if (!evaluation.IsSuccess)
        {
            return Task.FromResult(
                evaluation.FailureClassification == StepExecutionFailureClassification.InvalidConfiguration
                    ? StepExecutionResult.InvalidConfiguration(evaluation.Error!)
                    : StepExecutionResult.InputResolutionFailure(evaluation.Error!));
        }

        var output = JsonSerializer.Serialize(new ConditionalStepOutput
        {
            Matched = evaluation.Matched,
            SourcePath = configuration.SourcePath,
            Operator = configuration.Operator,
            ActualValue = evaluation.ActualValue,
            ExpectedValue = configuration.ExpectedValue,
            FalseOutcome = configuration.FalseOutcome
        }, JsonSerializerOptions);

        if (evaluation.Matched)
            return Task.FromResult(StepExecutionResult.Success(output));

        return Task.FromResult(
            configuration.FalseOutcome == ConditionalFalseOutcome.CancelWorkflow
                ? StepExecutionResult.CancelWorkflow(output)
                : StepExecutionResult.CompleteWorkflow(output));
    }

    private static ConditionalStepConfiguration DeserializeConfiguration(ConditionalStepConfigurationSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.SourcePath))
        {
            return new ConditionalStepConfiguration(
                snapshot.SourcePath,
                snapshot.Operator,
                snapshot.ExpectedValue,
                snapshot.FalseOutcome);
        }

        return new ConditionalStepConfiguration(
            snapshot.ConditionExpression ?? string.Empty,
            snapshot.FalseOutcome);
    }

    private static ConditionEvaluationResult EvaluateCondition(
        StepExecutionRequest request,
        ConditionalStepConfiguration configuration)
    {
        var resolvedValue = request.ResolveValueReference(
            configuration.SourcePath,
            $"conditional source '{configuration.SourcePath}'");

        if (!resolvedValue.IsSuccess)
        {
            if (resolvedValue.FailureClassification == StepExecutionFailureClassification.InvalidConfiguration)
                return ConditionEvaluationResult.InvalidConfiguration(resolvedValue.Error!);

            return configuration.Operator switch
            {
                ConditionalOperator.Exists => ConditionEvaluationResult.Success(false, null),
                ConditionalOperator.NotExists => ConditionEvaluationResult.Success(true, null),
                _ => ConditionEvaluationResult.Success(false, null)
            };
        }

        return configuration.Operator switch
        {
            ConditionalOperator.Exists => ConditionEvaluationResult.Success(true, ToComparableString(resolvedValue.Value!.Value, allowNonScalar: true)),
            ConditionalOperator.NotExists => ConditionEvaluationResult.Success(false, ToComparableString(resolvedValue.Value!.Value, allowNonScalar: true)),
            ConditionalOperator.Equals => CompareScalar(resolvedValue.Value!.Value, configuration.ExpectedValue!, expectedMatch: true),
            ConditionalOperator.NotEquals => CompareScalar(resolvedValue.Value!.Value, configuration.ExpectedValue!, expectedMatch: false),
            _ => ConditionEvaluationResult.InvalidConfiguration(
                $"Step '{request.StepKey}': conditional operator '{configuration.Operator}' is not supported.")
        };
    }

    private static ConditionEvaluationResult CompareScalar(
        JsonElement actualValue,
        string expectedValue,
        bool expectedMatch)
    {
        var comparableActual = ToComparableString(actualValue, allowNonScalar: false);
        if (comparableActual is null)
        {
            return ConditionEvaluationResult.InvalidConfiguration(
                "Conditional equals/not-equals operators require the resolved value to be a scalar (string, number, boolean, or null).");
        }

        var matched = string.Equals(comparableActual, expectedValue, StringComparison.Ordinal);
        return ConditionEvaluationResult.Success(expectedMatch ? matched : !matched, comparableActual);
    }

    private static string? ToComparableString(JsonElement value, bool allowNonScalar)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            JsonValueKind.Object or JsonValueKind.Array when allowNonScalar => value.GetRawText(),
            _ => null
        };
    }

    private static JsonSerializerOptions CreateJsonSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class ConditionalStepConfigurationSnapshot
    {
        public string? ConditionExpression { get; set; }
        public string? SourcePath { get; set; }
        public ConditionalOperator Operator { get; set; }
        public string? ExpectedValue { get; set; }
        public ConditionalFalseOutcome FalseOutcome { get; set; } = ConditionalFalseOutcome.CompleteWorkflow;
    }

    private sealed record ConditionEvaluationResult(
        bool IsSuccess,
        bool Matched,
        string? ActualValue,
        string? Error,
        StepExecutionFailureClassification? FailureClassification)
    {
        public static ConditionEvaluationResult Success(bool matched, string? actualValue) =>
            new(true, matched, actualValue, null, null);

        public static ConditionEvaluationResult InvalidConfiguration(string error) =>
            new(false, false, null, error, StepExecutionFailureClassification.InvalidConfiguration);
    }
}
