using System.Globalization;

namespace StepTrail.Shared.Definitions;

public static class DelayTimestampParser
{
    private static readonly string[] AcceptedFormats =
    [
        "O",
        "o",
        "yyyy-MM-dd'T'HH':'mm':'ss'Z'",
        "yyyy-MM-dd'T'HH':'mm':'ss.FFFFFFFK"
    ];

    public static bool TryParseUtcTimestamp(string value, out DateTimeOffset parsedUtc)
    {
        var trimmed = value.Trim();
        if (!HasUtcDesignatorOrOffset(trimmed))
        {
            parsedUtc = default;
            return false;
        }

        if (DateTimeOffset.TryParseExact(
            trimmed,
            AcceptedFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AdjustToUniversal,
            out var parsed))
        {
            parsedUtc = parsed.ToUniversalTime();
            return true;
        }

        parsedUtc = default;
        return false;
    }

    private static bool HasUtcDesignatorOrOffset(string value)
    {
        if (value.EndsWith('Z') || value.EndsWith('z'))
            return true;

        var timeSeparatorIndex = value.IndexOf('T');
        if (timeSeparatorIndex < 0)
            return false;

        var plusIndex = value.LastIndexOf('+');
        var minusIndex = value.LastIndexOf('-');
        var offsetIndex = Math.Max(plusIndex, minusIndex);

        return offsetIndex > timeSeparatorIndex;
    }
}
