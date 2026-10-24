using System.Text.Json;

namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// The one validation path for an <see cref="ImageStagingProfile"/>. The repository calls it on every write and on
/// every seed, so there is exactly one definition of a usable profile.
///
/// <para>
/// Every rule fails fast naming the offending value. None of them substitutes a default: a profile that cannot say
/// where a subject sits, or what camera to fall back to, is refused rather than repaired — a repaired profile would
/// produce a render nobody asked for and look identical to one that honoured the request.
/// </para>
/// </summary>
public static class ImageStagingProfileValidation
{
    /// <summary>The number of ladder entries a profile must declare at least one of; named for the message.</summary>
    public const int MinimumPlacementEntries = 1;

    public static void Validate(ImageStagingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            throw new InvalidOperationException(
                $"Image staging profile '{profile.Id}' has no display name; the /profiles page cannot list it.");
        }

        if (profile.Surface == ImageStagingSurface.Unknown)
        {
            throw new InvalidOperationException(
                $"Image staging profile '{profile.DisplayName}' must declare the surface it governs "
                + "(Composition or AssetManager).");
        }

        if (profile.ActiveDepth == FrameDepth.Unknown)
        {
            throw new InvalidOperationException(
                $"Image staging profile '{profile.DisplayName}' must declare the depth for an active subject; "
                + "it declares none, so the resolver would have to invent one.");
        }

        if (profile.ObserverDepth == FrameDepth.Unknown)
        {
            throw new InvalidOperationException(
                $"Image staging profile '{profile.DisplayName}' must declare the depth for an observer subject; "
                + "it declares none, so the resolver would have to invent one.");
        }

        var ladder = ReadPlacementLadder(profile);
        if (ladder.Count < MinimumPlacementEntries)
        {
            throw new InvalidOperationException(
                $"Image staging profile '{profile.DisplayName}' declares an empty placement ladder "
                + "(PlacementLadderJson); it must list at least one horizontal position.");
        }

        if (string.IsNullOrWhiteSpace(profile.DefaultCameraAngle))
        {
            throw new InvalidOperationException(
                $"Image staging profile '{profile.DisplayName}' declares no default camera angle, so a rationale with "
                + "no recognisable framing word would leave the camera unspecified.");
        }

        if (string.IsNullOrWhiteSpace(profile.DefaultShotSize))
        {
            throw new InvalidOperationException(
                $"Image staging profile '{profile.DisplayName}' declares no default shot size, so a rationale with "
                + "no recognisable framing word would leave the shot unspecified.");
        }

        if (string.IsNullOrWhiteSpace(profile.OmniscientShotSize))
        {
            throw new InvalidOperationException(
                $"Image staging profile '{profile.DisplayName}' declares no omniscient shot size, so an Omniscient "
                + "render would fall back to the composition default and lose the wide establishing lean (B-166 D7).");
        }

        // The camera height is configuration like the shot size: a blank or unrecognised value would leave the frame
        // without a height, and the render path refuses rather than guessing one.
        CameraHeights.Require(
            profile.DefaultCameraHeight,
            $"Image staging profile '{profile.DisplayName}' default camera height");

        ReadCameraKeywordMap(profile);
    }

    /// <summary>The ladder as positions, refusing an unknown or absent entry by name.</summary>
    /// <remarks>
    /// The entries are the positions a profile PERMITS, listed for editing convenience; the resolver chooses among
    /// them by cast size (one subject centred, a pair on the opposite edges, three filling the frame), so their order
    /// is not a seating priority.
    /// </remarks>
    public static IReadOnlyList<FrameHorizontal> ReadPlacementLadder(ImageStagingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var root = ParseArray(profile.PlacementLadderJson, nameof(profile.PlacementLadderJson), profile.DisplayName);

        var ladder = new List<FrameHorizontal>(root.GetArrayLength());
        foreach (var entry in root.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException(
                    $"Image staging profile '{profile.DisplayName}' has a non-string entry in "
                    + $"{nameof(profile.PlacementLadderJson)}; every entry must be a position name.");
            }

            var text = entry.GetString() ?? string.Empty;
            if (!Enum.TryParse<FrameHorizontal>(text, ignoreCase: true, out var horizontal)
                || horizontal == FrameHorizontal.Unknown)
            {
                throw new InvalidOperationException(
                    $"Image staging profile '{profile.DisplayName}' declares placement '{text}', which is not one of "
                    + $"{string.Join(", ", Enum.GetNames<FrameHorizontal>().Where(name => name != nameof(FrameHorizontal.Unknown)))}.");
            }

            if (ladder.Contains(horizontal))
            {
                throw new InvalidOperationException(
                    $"Image staging profile '{profile.DisplayName}' lists placement '{horizontal}' more than once; "
                    + "a ladder repeats no position, because two subjects sharing one position is the collision this "
                    + "feature removes.");
            }

            ladder.Add(horizontal);
        }

        return ladder;
    }

    /// <summary>
    /// The shot sizes this profile makes available to a Composer's framing control (B-161), in configured order.
    /// </summary>
    /// <remarks>
    /// The vocabulary is the PROFILE's — its default shot size followed by the distinct shot sizes its camera keyword
    /// map declares — so a framing an operator picks is one this surface can actually honour, and a new framing is
    /// added where every other staging decision is made (/profiles) rather than as a second list inside a page. The
    /// list is deduplicated case-insensitively because the keyword map legitimately maps several keywords to one shot
    /// size ("wide" and "wide shot" both meaning a wide establishing shot).
    /// </remarks>
    public static IReadOnlyList<string> ReadFramingChoices(ImageStagingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var choices = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(profile.DefaultShotSize) && seen.Add(profile.DefaultShotSize.Trim()))
        {
            choices.Add(profile.DefaultShotSize.Trim());
        }

        if (!string.IsNullOrWhiteSpace(profile.OmniscientShotSize) && seen.Add(profile.OmniscientShotSize.Trim()))
        {
            choices.Add(profile.OmniscientShotSize.Trim());
        }

        foreach (var keyword in ReadCameraKeywordMap(profile))
        {
            if (!string.IsNullOrWhiteSpace(keyword.ShotSize) && seen.Add(keyword.ShotSize.Trim()))
            {
                choices.Add(keyword.ShotSize.Trim());
            }
        }

        return choices;
    }

    /// <summary>The camera keyword map, refusing a malformed entry by name.</summary>
    public static IReadOnlyList<ImageStagingCameraKeyword> ReadCameraKeywordMap(ImageStagingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var root = ParseArray(profile.CameraKeywordMapJson, nameof(profile.CameraKeywordMapJson), profile.DisplayName);

        var keywords = new List<ImageStagingCameraKeyword>(root.GetArrayLength());
        foreach (var entry in root.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"Image staging profile '{profile.DisplayName}' has a non-object entry in "
                    + $"{nameof(profile.CameraKeywordMapJson)}; every entry must be an object.");
            }

            var keyword = entry.TryGetProperty("keyword", out var keywordElement) && keywordElement.ValueKind == JsonValueKind.String
                ? keywordElement.GetString() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(keyword))
            {
                throw new InvalidOperationException(
                    $"Image staging profile '{profile.DisplayName}' has a camera keyword entry with no keyword.");
            }

            var angle = entry.TryGetProperty("angle", out var angleElement) && angleElement.ValueKind == JsonValueKind.String
                ? angleElement.GetString()
                : null;
            var shotSize = entry.TryGetProperty("shotSize", out var shotElement) && shotElement.ValueKind == JsonValueKind.String
                ? shotElement.GetString()
                : null;
            var side = entry.TryGetProperty("side", out var sideElement) && sideElement.ValueKind == JsonValueKind.String
                ? sideElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(angle) && string.IsNullOrWhiteSpace(shotSize) && string.IsNullOrWhiteSpace(side))
            {
                throw new InvalidOperationException(
                    $"Image staging profile '{profile.DisplayName}' maps keyword '{keyword}' to no angle, shot size or "
                    + "side, so it would change nothing.");
            }

            keywords.Add(new ImageStagingCameraKeyword
            {
                Keyword = keyword,
                Angle = angle,
                ShotSize = shotSize,
                Side = side
            });
        }

        return keywords;
    }

    private static JsonElement ParseArray(string json, string field, string profileName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException(
                $"Image staging profile '{profileName}' has an empty {field}; it must be a JSON array (use \"[]\" for none).");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    $"Image staging profile '{profileName}' has a {field} that is not a JSON array.");
            }

            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Image staging profile '{profileName}' has a {field} that is not valid JSON: {exception.Message}");
        }
    }
}
