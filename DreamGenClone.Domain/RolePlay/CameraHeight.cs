namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// The camera's HEIGHT relative to the subject — one table, so every surface names the same five positions and every
/// model family states the one it understands.
/// </summary>
/// <remarks>
/// <para>
/// The set is deliberately five positions and not degrees. Research (2026-10-22): no official Qwen-Image document
/// defines camera height at all; the only official perspective keywords are <c>eye level perspective</c>,
/// <c>bird's eye perspective</c>, <c>low angle</c> and <c>aerial perspective</c> (Alibaba Model Studio text-to-image
/// prompt guide, which is the guide linked from the Qwen-Image API page). Degrees are documented as the WEAK input
/// ("distance measurements are more consistent than angular measurements"), and bare <c>worm's eye</c> is documented as
/// under-delivering — it produces a low angle rather than a ground-level upward view — which is why <see cref="Floor"/>
/// pairs the term with an explicit height clause.
/// </para>
/// <para>
/// <c>aerial</c> is deliberately absent: its official gloss is "landscape overview, geographic context", which reframes a
/// photograph of people as a landscape. <c>top-down</c> is absent for the same class of reason — it pulls toward a plan
/// / flat-lay view rather than a steep photograph of a person.
/// </para>
/// </remarks>
public static class CameraHeights
{
    /// <summary>Camera on or near the floor, looking up.</summary>
    public const string Floor = "floor";

    /// <summary>Camera at waist height, level with the subject.</summary>
    public const string Waist = "waist";

    /// <summary>Camera at the subject's eye height, level. The one official keyword among the five.</summary>
    public const string Eye = "eye";

    /// <summary>Camera above the subject's eye line, pitched down. Needs the qualifier: bare "high angle" is too weak.</summary>
    public const string Above = "above";

    /// <summary>Camera directly overhead, looking straight down. The most reliable of the five.</summary>
    public const string Overhead = "overhead";

    /// <summary>Every position, in ascending height order.</summary>
    public static IReadOnlyList<string> All { get; } = [Floor, Waist, Eye, Above, Overhead];

    /// <summary>The vertical aim each position implies: <c>up</c>, <c>level</c> or <c>down</c>.</summary>
    public static string PitchOf(string height) => height switch
    {
        Floor => "up",
        Waist => "level",
        Eye => "level",
        Above => "down",
        Overhead => "down",
        _ => throw new InvalidOperationException(
            $"Camera height '{height}' is not one of the recorded positions ({string.Join(", ", All)}).")
    };

    /// <summary>
    /// The phrase the image model is given, in natural-language photographic form: a term plus the height relation, in
    /// ONE clause. That shape is what both official Qwen prompt styles support — the Model Studio formula folds shot
    /// size, angle and lens into a single camera element, and Qwen's own rewriter writes sentences rather than tags.
    /// </summary>
    public static string Wording(string height) => height switch
    {
        Floor => "low angle from floor level, looking up",
        Waist => "waist height, level with the subject",
        Eye => "eye level perspective",
        Above => "high angle, camera just above eye level, looking down",
        Overhead => "bird's eye perspective, looking straight down",
        _ => throw new InvalidOperationException(
            $"Camera height '{height}' is not one of the recorded positions ({string.Join(", ", All)}).")
    };

    /// <summary>Validates a stored/selected value and returns it trimmed, failing fast on anything else.</summary>
    public static string Require(string? height, string source)
    {
        if (string.IsNullOrWhiteSpace(height))
        {
            throw new InvalidOperationException($"{source} names no camera height, so the frame cannot be stated.");
        }

        var value = height.Trim();
        if (!All.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{source} names camera height '{value}', which is not one of the recorded positions " +
                $"({string.Join(", ", All)}).");
        }

        return value.ToLowerInvariant();
    }

    /// <summary>True when the value names a recorded position; false for null/blank.</summary>
    public static bool IsDefined(string? height)
        => !string.IsNullOrWhiteSpace(height)
           && All.Contains(height.Trim(), StringComparer.OrdinalIgnoreCase);
}
