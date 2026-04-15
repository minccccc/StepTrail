namespace StepTrail.Shared.Runtime.Scheduling;

public sealed class SimpleCronExpression
{
    private readonly CronField _minute;
    private readonly CronField _hour;
    private readonly CronField _dayOfMonth;
    private readonly CronField _month;
    private readonly CronField _dayOfWeek;

    private SimpleCronExpression(
        string expression,
        CronField minute,
        CronField hour,
        CronField dayOfMonth,
        CronField month,
        CronField dayOfWeek)
    {
        Expression = expression;
        _minute = minute;
        _hour = hour;
        _dayOfMonth = dayOfMonth;
        _month = month;
        _dayOfWeek = dayOfWeek;
    }

    public string Expression { get; }

    public static bool TryParse(string expression, out SimpleCronExpression? cronExpression, out string? error)
    {
        cronExpression = null;
        error = null;

        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "Expression must not be empty.";
            return false;
        }

        var parts = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 5)
        {
            error = "Expression must contain exactly five fields: minute hour day-of-month month day-of-week.";
            return false;
        }

        if (!TryParseField(parts[0], 0, 59, allowSevenAsSunday: false, "minute", out var minute, out error)
            || !TryParseField(parts[1], 0, 23, allowSevenAsSunday: false, "hour", out var hour, out error)
            || !TryParseField(parts[2], 1, 31, allowSevenAsSunday: false, "day-of-month", out var dayOfMonth, out error)
            || !TryParseField(parts[3], 1, 12, allowSevenAsSunday: false, "month", out var month, out error)
            || !TryParseField(parts[4], 0, 6, allowSevenAsSunday: true, "day-of-week", out var dayOfWeek, out error))
        {
            return false;
        }

        if (!dayOfMonth.IsWildcard && !dayOfWeek.IsWildcard)
        {
            error = "First-version cron supports constraining either day-of-month or day-of-week, but not both.";
            return false;
        }

        cronExpression = new SimpleCronExpression(expression.Trim(), minute, hour, dayOfMonth, month, dayOfWeek);
        return true;
    }

    public DateTimeOffset? GetNextOccurrence(DateTimeOffset afterUtc)
    {
        var utcReference = afterUtc.ToUniversalTime();
        var candidate = new DateTimeOffset(
                utcReference.Year,
                utcReference.Month,
                utcReference.Day,
                utcReference.Hour,
                utcReference.Minute,
                0,
                TimeSpan.Zero)
            .AddMinutes(1);
        var upperBound = candidate.AddYears(5);

        for (; candidate <= upperBound; candidate = candidate.AddMinutes(1))
        {
            if (Matches(candidate))
                return candidate;
        }

        return null;
    }

    private bool Matches(DateTimeOffset candidateUtc)
    {
        var dayOfWeek = (int)candidateUtc.DayOfWeek;

        return _minute.Matches(candidateUtc.Minute)
               && _hour.Matches(candidateUtc.Hour)
               && _month.Matches(candidateUtc.Month)
               && _dayOfMonth.Matches(candidateUtc.Day)
               && _dayOfWeek.Matches(dayOfWeek);
    }

    private static bool TryParseField(
        string rawValue,
        int min,
        int max,
        bool allowSevenAsSunday,
        string fieldName,
        out CronField field,
        out string? error)
    {
        field = default;
        error = null;

        if (rawValue == "*")
        {
            field = CronField.Wildcard;
            return true;
        }

        var allowedValues = new SortedSet<int>();
        var segments = rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            error = $"Cron {fieldName} field must not be empty.";
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment.Contains('/'))
            {
                var stepParts = segment.Split('/', StringSplitOptions.TrimEntries);
                if (stepParts.Length != 2 || stepParts[0] != "*")
                {
                    error = $"Cron {fieldName} field only supports step syntax in the form '*/n'.";
                    return false;
                }

                if (!int.TryParse(stepParts[1], out var step) || step < 1)
                {
                    error = $"Cron {fieldName} field step value must be a positive integer.";
                    return false;
                }

                for (var value = min; value <= max; value += step)
                    allowedValues.Add(value);

                continue;
            }

            if (segment.Contains('-'))
            {
                var rangeParts = segment.Split('-', StringSplitOptions.TrimEntries);
                if (rangeParts.Length != 2
                    || !TryNormalizeValue(rangeParts[0], min, max, allowSevenAsSunday, out var rangeStart)
                    || !TryNormalizeValue(rangeParts[1], min, max, allowSevenAsSunday, out var rangeEnd))
                {
                    error = $"Cron {fieldName} field contains an invalid range segment '{segment}'.";
                    return false;
                }

                if (rangeStart > rangeEnd)
                {
                    error = $"Cron {fieldName} field range '{segment}' must have a start less than or equal to the end.";
                    return false;
                }

                for (var value = rangeStart; value <= rangeEnd; value++)
                    allowedValues.Add(value);

                continue;
            }

            if (!TryNormalizeValue(segment, min, max, allowSevenAsSunday, out var singleValue))
            {
                error = $"Cron {fieldName} field contains an invalid value '{segment}'.";
                return false;
            }

            allowedValues.Add(singleValue);
        }

        field = new CronField(false, allowedValues.ToArray());
        return true;
    }

    private static bool TryNormalizeValue(
        string rawValue,
        int min,
        int max,
        bool allowSevenAsSunday,
        out int normalizedValue)
    {
        normalizedValue = default;

        if (!int.TryParse(rawValue, out var parsedValue))
            return false;

        if (allowSevenAsSunday && parsedValue == 7)
        {
            normalizedValue = 0;
            return true;
        }

        if (parsedValue < min || parsedValue > max)
            return false;

        normalizedValue = parsedValue;
        return true;
    }

    private readonly record struct CronField(bool IsWildcard, int[] AllowedValues)
    {
        public static CronField Wildcard => new(true, []);

        public bool Matches(int value) =>
            IsWildcard || Array.BinarySearch(AllowedValues, value) >= 0;
    }
}
