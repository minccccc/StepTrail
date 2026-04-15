using System.Text.Json.Serialization;

namespace StepTrail.Shared.Definitions;

/// <summary>
/// Backoff strategy used between retry attempts.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackoffStrategy
{
    /// <summary>
    /// Wait a fixed delay between every retry attempt.
    /// </summary>
    Fixed = 1,

    /// <summary>
    /// Exponentially increase the delay between attempts: initialDelay * 2^(attempt-1),
    /// capped at <see cref="RetryPolicy.MaxDelaySeconds"/>.
    /// </summary>
    Exponential = 2
}

/// <summary>
/// Defines how a failed step should be retried by the runtime.
///
/// Every step gets an effective retry policy: either an explicit per-step policy
/// or the inherited global/workflow default.
///
/// Design rules:
///   - MaxAttempts includes the initial attempt (MaxAttempts=1 means no retry).
///   - Delay computation is deterministic and testable.
///   - The policy is independent of any step type.
///   - RetryOnTimeout controls whether platform-level timeouts are retryable.
/// </summary>
public sealed class RetryPolicy
{
    /// <summary>
    /// Default policy used when no explicit policy is configured.
    /// 3 attempts, 10s fixed delay, platform timeouts are retryable.
    /// </summary>
    public static readonly RetryPolicy Default = new(
        maxAttempts: 3,
        initialDelaySeconds: 10,
        backoffStrategy: BackoffStrategy.Fixed,
        retryOnTimeout: true);

    /// <summary>
    /// Policy that disables all retries. One attempt only.
    /// </summary>
    public static readonly RetryPolicy NoRetry = new(
        maxAttempts: 1,
        initialDelaySeconds: 0,
        backoffStrategy: BackoffStrategy.Fixed,
        retryOnTimeout: false);

    [JsonConstructor]
    public RetryPolicy(
        int maxAttempts,
        int initialDelaySeconds,
        BackoffStrategy backoffStrategy,
        bool retryOnTimeout = true,
        int? maxDelaySeconds = null)
    {
        if (maxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "MaxAttempts must be at least 1.");
        if (initialDelaySeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(initialDelaySeconds), "InitialDelaySeconds must not be negative.");
        if (!Enum.IsDefined(backoffStrategy))
            throw new ArgumentOutOfRangeException(nameof(backoffStrategy), "BackoffStrategy must be a defined enum value.");
        if (maxDelaySeconds.HasValue && maxDelaySeconds.Value < 1)
            throw new ArgumentOutOfRangeException(nameof(maxDelaySeconds), "MaxDelaySeconds must be at least 1 when specified.");
        if (maxDelaySeconds.HasValue && maxDelaySeconds.Value < initialDelaySeconds)
            throw new ArgumentOutOfRangeException(nameof(maxDelaySeconds), "MaxDelaySeconds must not be less than InitialDelaySeconds.");

        MaxAttempts = maxAttempts;
        InitialDelaySeconds = initialDelaySeconds;
        BackoffStrategy = backoffStrategy;
        RetryOnTimeout = retryOnTimeout;
        MaxDelaySeconds = maxDelaySeconds;
    }

    /// <summary>
    /// Total number of attempts (including the initial one). MaxAttempts=1 means no retry.
    /// </summary>
    public int MaxAttempts { get; }

    /// <summary>
    /// Delay in seconds before the first retry. For Fixed strategy this is the constant delay.
    /// For Exponential this is the base delay that doubles per attempt.
    /// </summary>
    public int InitialDelaySeconds { get; }

    /// <summary>
    /// Strategy for computing delay between retries.
    /// </summary>
    public BackoffStrategy BackoffStrategy { get; }

    /// <summary>
    /// Whether platform-level step timeouts (handler exceeded its configured timeout)
    /// should be treated as retryable failures. Default true.
    /// </summary>
    public bool RetryOnTimeout { get; }

    /// <summary>
    /// Upper bound on retry delay in seconds. Only meaningful for Exponential backoff.
    /// Null means no cap (exponential growth continues).
    /// </summary>
    public int? MaxDelaySeconds { get; }

    /// <summary>
    /// Whether any retries are configured (MaxAttempts > 1).
    /// </summary>
    public bool HasRetries => MaxAttempts > 1;

    /// <summary>
    /// Computes the delay in seconds before the given retry attempt.
    /// <paramref name="currentAttempt"/> is the attempt that just failed (1-based).
    /// Returns the number of seconds to wait before the next attempt.
    /// </summary>
    public int ComputeDelaySeconds(int currentAttempt)
    {
        if (currentAttempt < 1)
            throw new ArgumentOutOfRangeException(nameof(currentAttempt), "Attempt number must be at least 1.");

        var delay = BackoffStrategy switch
        {
            BackoffStrategy.Fixed => InitialDelaySeconds,
            BackoffStrategy.Exponential => ComputeExponentialDelay(currentAttempt),
            _ => InitialDelaySeconds
        };

        if (MaxDelaySeconds.HasValue && delay > MaxDelaySeconds.Value)
            delay = MaxDelaySeconds.Value;

        return delay;
    }

    private int ComputeExponentialDelay(int currentAttempt)
    {
        // Retry number (0-based): first retry after attempt 1 = retry 0
        var retryIndex = currentAttempt - 1;

        // Guard against overflow: cap the shift at 30 (2^30 * any reasonable base > int.MaxValue)
        if (retryIndex > 30)
            return MaxDelaySeconds ?? int.MaxValue;

        var multiplier = 1 << retryIndex; // 2^retryIndex
        var delay = (long)InitialDelaySeconds * multiplier;

        return delay > int.MaxValue ? (MaxDelaySeconds ?? int.MaxValue) : (int)delay;
    }
}
