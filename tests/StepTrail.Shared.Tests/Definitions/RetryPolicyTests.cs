using StepTrail.Shared.Definitions;
using Xunit;

namespace StepTrail.Shared.Tests.Definitions;

public class RetryPolicyTests
{
    [Fact]
    public void Default_HasExpectedValues()
    {
        var policy = RetryPolicy.Default;

        Assert.Equal(3, policy.MaxAttempts);
        Assert.Equal(10, policy.InitialDelaySeconds);
        Assert.Equal(BackoffStrategy.Fixed, policy.BackoffStrategy);
        Assert.True(policy.RetryOnTimeout);
        Assert.Null(policy.MaxDelaySeconds);
        Assert.True(policy.HasRetries);
    }

    [Fact]
    public void NoRetry_HasExpectedValues()
    {
        var policy = RetryPolicy.NoRetry;

        Assert.Equal(1, policy.MaxAttempts);
        Assert.Equal(0, policy.InitialDelaySeconds);
        Assert.False(policy.RetryOnTimeout);
        Assert.False(policy.HasRetries);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_Throws_WhenMaxAttemptsIsLessThan1(int maxAttempts)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RetryPolicy(maxAttempts, 10, BackoffStrategy.Fixed));

        Assert.Equal("maxAttempts", ex.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-10)]
    public void Constructor_Throws_WhenInitialDelayIsNegative(int delay)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RetryPolicy(3, delay, BackoffStrategy.Fixed));

        Assert.Equal("initialDelaySeconds", ex.ParamName);
    }

    [Fact]
    public void Constructor_Throws_WhenMaxDelayIsLessThan1()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RetryPolicy(3, 10, BackoffStrategy.Exponential, maxDelaySeconds: 0));

        Assert.Equal("maxDelaySeconds", ex.ParamName);
    }

    [Fact]
    public void Constructor_Throws_WhenMaxDelayIsLessThanInitialDelay()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RetryPolicy(3, 30, BackoffStrategy.Exponential, maxDelaySeconds: 10));

        Assert.Equal("maxDelaySeconds", ex.ParamName);
    }

    [Fact]
    public void Constructor_AllowsZeroInitialDelay()
    {
        var policy = new RetryPolicy(3, 0, BackoffStrategy.Fixed);

        Assert.Equal(0, policy.InitialDelaySeconds);
    }

    [Fact]
    public void Constructor_AllowsMaxAttempts1()
    {
        var policy = new RetryPolicy(1, 10, BackoffStrategy.Fixed);

        Assert.Equal(1, policy.MaxAttempts);
        Assert.False(policy.HasRetries);
    }

    // --- Fixed backoff ---

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void ComputeDelaySeconds_Fixed_ReturnsConstantDelay(int attempt)
    {
        var policy = new RetryPolicy(10, 15, BackoffStrategy.Fixed);

        Assert.Equal(15, policy.ComputeDelaySeconds(attempt));
    }

    // --- Exponential backoff ---

    [Fact]
    public void ComputeDelaySeconds_Exponential_DoublesPerAttempt()
    {
        var policy = new RetryPolicy(5, 5, BackoffStrategy.Exponential);

        Assert.Equal(5, policy.ComputeDelaySeconds(1));   // 5 * 2^0 = 5
        Assert.Equal(10, policy.ComputeDelaySeconds(2));  // 5 * 2^1 = 10
        Assert.Equal(20, policy.ComputeDelaySeconds(3));  // 5 * 2^2 = 20
        Assert.Equal(40, policy.ComputeDelaySeconds(4));  // 5 * 2^3 = 40
    }

    [Fact]
    public void ComputeDelaySeconds_Exponential_RespectsCap()
    {
        var policy = new RetryPolicy(5, 5, BackoffStrategy.Exponential, maxDelaySeconds: 30);

        Assert.Equal(5, policy.ComputeDelaySeconds(1));    // 5 * 2^0 = 5
        Assert.Equal(10, policy.ComputeDelaySeconds(2));   // 5 * 2^1 = 10
        Assert.Equal(20, policy.ComputeDelaySeconds(3));   // 5 * 2^2 = 20
        Assert.Equal(30, policy.ComputeDelaySeconds(4));   // 5 * 2^3 = 40 → capped at 30
    }

    [Fact]
    public void ComputeDelaySeconds_Exponential_HandlesLargeAttemptNumber()
    {
        var policy = new RetryPolicy(100, 1, BackoffStrategy.Exponential, maxDelaySeconds: 3600);

        var delay = policy.ComputeDelaySeconds(50);

        Assert.Equal(3600, delay);
    }

    [Fact]
    public void ComputeDelaySeconds_Fixed_RespectsCap()
    {
        // MaxDelay is meaningful even for fixed — guards misconfigured policies.
        var policy = new RetryPolicy(3, 60, BackoffStrategy.Fixed, maxDelaySeconds: 60);

        Assert.Equal(60, policy.ComputeDelaySeconds(1));
    }

    [Fact]
    public void ComputeDelaySeconds_Throws_WhenAttemptIsZero()
    {
        var policy = RetryPolicy.Default;

        Assert.Throws<ArgumentOutOfRangeException>(() => policy.ComputeDelaySeconds(0));
    }
}
