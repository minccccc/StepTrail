using StepTrail.Shared.Runtime.Scheduling;
using Xunit;

namespace StepTrail.Shared.Tests.Runtime;

public class SimpleCronExpressionTests
{
    [Fact]
    public void TryParse_ReturnsTrue_ForDailyAtFixedTime()
    {
        var parsed = SimpleCronExpression.TryParse("0 8 * * *", out var expression, out var error);

        Assert.True(parsed);
        Assert.NotNull(expression);
        Assert.Null(error);
    }

    [Fact]
    public void TryParse_ReturnsFalse_WhenDayOfMonthAndDayOfWeekAreBothConstrained()
    {
        var parsed = SimpleCronExpression.TryParse("0 8 1 * 1", out _, out var error);

        Assert.False(parsed);
        Assert.Contains("either day-of-month or day-of-week", error, StringComparison.Ordinal);
    }

    [Fact]
    public void GetNextOccurrence_ReturnsTopOfNextHour_ForHourlyCron()
    {
        SimpleCronExpression.TryParse("0 * * * *", out var expression, out _);

        var nextOccurrence = expression!.GetNextOccurrence(
            new DateTimeOffset(2026, 4, 14, 10, 17, 45, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 4, 14, 11, 0, 0, TimeSpan.Zero), nextOccurrence);
    }

    [Fact]
    public void GetNextOccurrence_ReturnsNextWeekday_ForWeekdayCron()
    {
        SimpleCronExpression.TryParse("30 9 * * 1-5", out var expression, out _);

        var nextOccurrence = expression!.GetNextOccurrence(
            new DateTimeOffset(2026, 4, 17, 10, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 4, 20, 9, 30, 0, TimeSpan.Zero), nextOccurrence);
    }
}
