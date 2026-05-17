using System.Globalization;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Shared band eligibility matching helper.
/// FR-010: band-parsing logic must be encapsulated here; do not duplicate in other locations.
/// </summary>
public static class FinishingMoveBandMatcher
{
    /// <summary>
    /// Returns true if <paramref name="statValue"/> falls within any band listed in
    /// <paramref name="eligibleBands"/>. A null/empty value means "any" — always returns true.
    /// Multiple bands are separated by commas.
    /// </summary>
    public static bool MatchesBandEligibility(string? eligibleBands, double statValue)
    {
        if (string.IsNullOrWhiteSpace(eligibleBands))
            return true;

        return eligibleBands
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(part => MatchesNumericBand(part, statValue));
    }

    public static bool MatchesNumericBand(string? band, double value)
    {
        if (string.IsNullOrWhiteSpace(band))
        {
            return true;
        }

        var normalized = band.Trim();
        if (string.Equals(normalized, "any", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "all", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "*", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var lower = normalized.ToLowerInvariant();
        if (lower.Contains('|'))
        {
            return lower
                .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(part => MatchesNumericBand(part, value));
        }

        if (TryMatchNamedBand(lower, value, out var namedBandMatch))
        {
            return namedBandMatch;
        }

        if (TryParseRangeBand(lower, out var min, out var max))
        {
            return value >= min && value <= max;
        }

        if (lower.StartsWith(">=", StringComparison.Ordinal) && double.TryParse(lower[2..], NumberStyles.Float, CultureInfo.InvariantCulture, out var minInclusive))
        {
            return value >= minInclusive;
        }

        if (lower.StartsWith(">", StringComparison.Ordinal) && double.TryParse(lower[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out var minExclusive))
        {
            return value > minExclusive;
        }

        if (lower.StartsWith("<=", StringComparison.Ordinal) && double.TryParse(lower[2..], NumberStyles.Float, CultureInfo.InvariantCulture, out var maxInclusive))
        {
            return value <= maxInclusive;
        }

        if (lower.StartsWith("<", StringComparison.Ordinal) && double.TryParse(lower[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out var maxExclusive))
        {
            return value < maxExclusive;
        }

        if (lower.EndsWith('+') && double.TryParse(lower[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var plusMin))
        {
            return value >= plusMin;
        }

        if (double.TryParse(lower, NumberStyles.Float, CultureInfo.InvariantCulture, out var exact))
        {
            return Math.Abs(value - exact) < 0.0001;
        }

        return false;
    }

    public static bool TryMatchNamedBand(string lowerBand, double value, out bool matches)
    {
        switch (lowerBand)
        {
            case "low":
                matches = value < 34d;
                return true;
            case "medium":
            case "mid":
                matches = value >= 34d && value < 67d;
                return true;
            case "high":
                matches = value >= 67d;
                return true;
            default:
                matches = false;
                return false;
        }
    }

    public static bool TryParseRangeBand(string lowerBand, out double min, out double max)
    {
        min = 0;
        max = 0;

        var separators = new[] { "-", " to ", "..", "~" };
        foreach (var separator in separators)
        {
            var pieces = lowerBand.Split(separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length != 2)
            {
                continue;
            }

            if (!double.TryParse(pieces[0], NumberStyles.Float, CultureInfo.InvariantCulture, out min)
                || !double.TryParse(pieces[1], NumberStyles.Float, CultureInfo.InvariantCulture, out max))
            {
                continue;
            }

            if (max < min)
            {
                (min, max) = (max, min);
            }

            return true;
        }

        return false;
    }
}
