using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The ONE owner of the image-tag vocabulary: the axis prefixes, the normalization rule, and the words each pose
/// metadata value is tagged with.
///
/// <para>
/// Tags exist so an image can be FOUND later — by character, by what the figure is doing, by angle, by rating, by
/// wardrobe, by which LoRA was in the stack — and so "show me every NSFW kneeling reference" is a query rather than a
/// walk through thumbnails. That only works if every writer spells a tag the same way, so the spelling lives here and
/// nothing else is allowed to compose tag text.
/// </para>
///
/// <para>
/// <b>Shape:</b> <c>prefix:value</c>, e.g. <c>stance:kneeling</c>, <c>rating:nsfw</c>, <c>character:becky</c>. The
/// prefix is what makes the tag self-describing AND what makes an exact search possible: a search for
/// <c>kneeling</c> normalizes to <c>kneeling</c> and matches the tag's value, without <c>kneeling</c> in a character's
/// name matching by accident.
/// </para>
///
/// <para>
/// <b>Normalization</b> is deliberately lossy and stable: lower-case, every run of non-alphanumeric characters
/// becomes one <c>-</c>, ends trimmed. So <c>"3/4 left"</c> is <c>3-4-left</c> and <c>"T-pose"</c> is <c>t-pose</c>,
/// and the same words typed into a search box match. A tag is never stored with its display capitalization, because a
/// stored tag is a KEY, not a label — the label is what a UI shows (see <see cref="Describe"/>).
/// </para>
///
/// <para>
/// It lives in the WEB layer because it reuses the pose library's own label owner
/// (<see cref="PoseMetadataLabels"/>), which is where the words in the pose library come from: an image tag and a pose
/// keyword must be the same word, or searching for one would silently miss the other. The storage layer takes a plain
/// list of already-formed tags, so the vocabulary is decided in exactly one place and the database knows nothing about
/// it.
/// </para>
///
/// <para>
/// <b>"Not declared" is not a tag.</b> <see cref="PoseStance.Unknown"/>, <see cref="PoseFacingDirection.Unknown"/>,
/// <see cref="PoseCameraAngle.Unknown"/> and <see cref="PoseContentRating.Unrated"/> produce NO tag, because a tag
/// saying <c>stance:unknown</c> would be a search hit for a fact nobody stated. The absence is the honest record, and
/// every consumer already treats an untagged image as "this was not declared" rather than as a default.
/// </para>
/// </summary>
public static class ImageTagCatalog
{
    /// <summary>Whose image this is — a character, a scene, a wardrobe item. Value is the owner's display name.</summary>
    public const string Character = "character";

    /// <summary>The pose preset's NAME, so an image is findable by the pose it rendered rather than only by its id.</summary>
    public const string Pose = "pose";

    /// <summary>The pose preset's CATEGORY text, which is where a library's own grouping words live.</summary>
    public const string Category = "category";

    /// <summary>The pose library the preset came from.</summary>
    public const string Library = "library";

    /// <summary>What the figure is doing with its body (<see cref="PoseStance"/>).</summary>
    public const string Stance = "stance";

    /// <summary>Which way the figure faces (<see cref="PoseFacingDirection"/>).</summary>
    public const string Direction = "direction";

    /// <summary>Where the camera sits (<see cref="PoseCameraAngle"/>).</summary>
    public const string Camera = "camera";

    /// <summary>Clothed / unclothed (<see cref="PoseContentRating"/>). The word a search for "nsfw" must hit.</summary>
    public const string Rating = "rating";

    /// <summary>A named position an image stands for — a catalog position, a suite cell, an encounter beat.</summary>
    public const string Position = "position";

    /// <summary>The catalog VARIANT an image was rendered from (the per-model wording set, e.g. <c>biglust</c>).</summary>
    public const string Variant = "variant";

    /// <summary>
    /// A sex position or act (missionary, doggy, …), which is DECLARED and never inferred: nothing in the keypoints or
    /// the prompt says it reliably, and a guessed act tag makes an image appear under a search it does not belong to.
    /// </summary>
    public const string SexPosition = "sex";

    /// <summary>A wardrobe asset the image's character was dressed from.</summary>
    public const string Wardrobe = "wardrobe";

    /// <summary>A location asset the image was rendered in.</summary>
    public const string Location = "location";

    /// <summary>An expression preset that shaped the render (its key's own word, e.g. <c>expression:laughing</c>).</summary>
    public const string Expression = "expression";

    /// <summary>A lighting preset that shaped the render.</summary>
    public const string Lighting = "lighting";

    /// <summary>A character LoRA (identity) that was in the stack.</summary>
    public const string CharacterLora = "lora";

    /// <summary>A scene LoRA (unlock / act / anatomy / style) that was in the stack, tagged by its catalog purpose.</summary>
    public const string SceneLora = "scene-lora";

    /// <summary>The registered model that rendered the image.</summary>
    public const string Model = "model";

    /// <summary>A reference image or asset the render conditioned on.</summary>
    public const string Reference = "reference";

    /// <summary>What produced the image: <c>suite</c>, <c>composer</c>, <c>upload</c>, <c>edit</c>, <c>catalog</c>.</summary>
    public const string Source = "source";

    /// <summary>
    /// Every prefix this vocabulary recognises, in the order a UI should present them. A tag whose prefix is not in
    /// this list is not a catalog tag, which is what lets a hand-typed tag be refused rather than stored unsearchable.
    /// </summary>
    public static readonly IReadOnlyList<string> Prefixes =
    [
        Character, Pose, Category, Library, Stance, Direction, Camera, Rating,
        Position, Variant, SexPosition, Wardrobe, Location, Expression, Lighting,
        CharacterLora, SceneLora, Model, Reference, Source
    ];

    /// <summary>
    /// Lower-cases, replaces every run of characters outside <c>[a-z0-9]</c> with a single <c>-</c>, and trims the
    /// dashes. Returns the empty string when nothing survives, which callers treat as "no tag" rather than as a tag
    /// made of punctuation.
    /// </summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(raw.Length);
        var pendingDash = false;
        foreach (var ch in raw)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                if (pendingDash && builder.Length > 0)
                {
                    builder.Append('-');
                }

                pendingDash = false;
                builder.Append(char.ToLowerInvariant(ch));
                continue;
            }

            // A dash is DEFERRED rather than appended: that is what collapses "3 / 4  left" into one separator and
            // keeps a trailing separator out of the result.
            pendingDash = true;
        }

        return builder.ToString();
    }

    /// <summary>
    /// The tag for a value on one axis, or null when the value normalizes to nothing (or was never stated). Null is
    /// the caller's signal to omit the tag entirely — never to write a placeholder.
    /// </summary>
    public static string? Tag(string prefix, string? value)
    {
        if (!Prefixes.Contains(prefix, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{prefix}' is not a tag prefix in the catalog, so a tag written with it could never be searched for. "
                + $"Known prefixes: {string.Join(", ", Prefixes)}.");
        }

        var normalized = Normalize(value);
        return normalized.Length == 0 ? null : $"{prefix}:{normalized}";
    }

    public static string? StanceTag(PoseStance stance) => stance == PoseStance.Unknown
        ? null
        : Tag(ImageTagCatalog.Stance, PoseMetadataLabels.Stance(stance));

    public static string? DirectionTag(PoseFacingDirection direction) => direction == PoseFacingDirection.Unknown
        ? null
        : Tag(ImageTagCatalog.Direction, PoseMetadataLabels.Direction(direction));

    public static string? CameraTag(PoseCameraAngle camera) => camera == PoseCameraAngle.Unknown
        ? null
        : Tag(ImageTagCatalog.Camera, PoseMetadataLabels.Camera(camera));

    public static string? RatingTag(PoseContentRating rating) => rating == PoseContentRating.Unrated
        ? null
        : Tag(ImageTagCatalog.Rating, PoseMetadataLabels.Rating(rating));

    /// <summary>
    /// The stance tag for a stance recorded as its ENUM NAME — the shape a queued render carries when it was
    /// conditioned on a bare stance rather than on a library preset (the payload stores the enum name).
    ///
    /// <para>
    /// Parsed rather than slugged so it lands on the SAME tag a preset produces: slugging "AllFours" directly would
    /// write <c>stance:allfours</c>, and a search for "all fours" would then miss half the library. A name that parses
    /// to nothing (including <see cref="PoseStance.Unknown"/>) yields no tag, exactly as an undeclared stance does.
    /// </para>
    /// </summary>
    public static string? StanceFromName(string? stanceName)
    {
        if (string.IsNullOrWhiteSpace(stanceName))
        {
            return null;
        }

        var trimmed = stanceName.Trim();
        if (trimmed.All(char.IsAsciiDigit))
        {
            // A bare number is not a stance name; Enum.TryParse would happily read "5" as AllFours.
            return null;
        }

        return Enum.TryParse<PoseStance>(trimmed, ignoreCase: true, out var stance) ? StanceTag(stance) : null;
    }

    /// <summary>
    /// The tags one pose preset contributes: its name, its category, its library, and the four metadata axes. Anything
    /// the preset does not declare contributes nothing (see the type's own note on "not declared").
    /// </summary>
    public static IEnumerable<string> FromPosePreset(PosePreset preset, string? libraryName = null)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var tags = new[]
        {
            Tag(Pose, preset.Name),
            Tag(Category, preset.Category),
            Tag(Library, libraryName),
            StanceTag(preset.Stance),
            DirectionTag(preset.Direction),
            CameraTag(preset.CameraAngle),
            RatingTag(preset.ContentRating)
        };

        return tags.Where(tag => tag is not null).Select(tag => tag!);
    }

    /// <summary>
    /// Cleans a whole tag list: drops null/blank entries, normalizes each tag's VALUE, drops anything whose prefix is
    /// not in the catalog, and removes duplicates while preserving the order they were supplied in (which is what makes
    /// the stored list reproducible instead of incidental).
    /// </summary>
    public static IReadOnlyList<string> NormalizeAll(IEnumerable<string?>? tags)
    {
        if (tags is null)
        {
            return [];
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            var separator = tag.IndexOf(':');
            if (separator <= 0 || separator == tag.Length - 1)
            {
                throw new InvalidOperationException(
                    $"'{tag}' is not a catalog tag: every tag is 'prefix:value', and the prefix is what makes it "
                    + "searchable by axis.");
            }

            var prefix = tag[..separator].Trim().ToLowerInvariant();
            var normalized = Tag(prefix, tag[(separator + 1)..]);
            if (normalized is not null && seen.Add(normalized))
            {
                result.Add(normalized);
            }
        }

        return result;
    }

    /// <summary>True when a stored tag belongs to this vocabulary (used to reject a hand-typed tag).</summary>
    public static bool IsCatalogTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var separator = tag.IndexOf(':');
        return separator > 0
            && separator < tag.Length - 1
            && Prefixes.Contains(tag[..separator], StringComparer.Ordinal)
            && Normalize(tag[(separator + 1)..]).Length > 0;
    }

    /// <summary>The prefix of a stored tag, for grouping or for a chip's axis label; null when it is not a tag.</summary>
    public static string? PrefixOf(string? tag)
        => IsCatalogTag(tag) ? tag![..tag!.IndexOf(':')] : null;

    /// <summary>The value of a stored tag, with its dashes turned back into spaces for display.</summary>
    public static string Describe(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        var separator = tag.IndexOf(':');
        var value = separator > 0 ? tag[(separator + 1)..] : tag;
        return value.Replace('-', ' ');
    }

    /// <summary>
    /// Parses a stored <c>TagsJson</c> array. Unreadable or absent JSON reads as NO tags: an image whose tag list
    /// cannot be parsed has no usable tags, and inventing one from the raw text would put a broken row into a search
    /// result as if it had matched.
    ///
    /// <para>
    /// Lenient where the WRITE path is strict, and deliberately so: this runs on a read (a list of images), where a row
    /// carrying a tag shape the catalog no longer knows must be skipped rather than thrown out of the whole list. A
    /// write that receives such a tag still fails by name through <see cref="NormalizeAll"/>.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Parse(string? tagsJson)
    {
        if (string.IsNullOrWhiteSpace(tagsJson))
        {
            return [];
        }

        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<List<string>>(tagsJson);
            if (parsed is null)
            {
                return [];
            }

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tag in parsed)
            {
                if (!IsCatalogTag(tag))
                {
                    continue;
                }

                var separator = tag!.IndexOf(':');
                var normalized = Tag(tag[..separator], tag[(separator + 1)..]);
                if (normalized is not null && seen.Add(normalized))
                {
                    result.Add(normalized);
                }
            }

            return result;
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// The JSON form a tag list is STORED as, which is the one place that shape is decided. The list is normalized
    /// first, so nothing can be stored in a shape no search would find.
    /// </summary>
    public static string Serialize(IEnumerable<string?>? tags)
        => System.Text.Json.JsonSerializer.Serialize(NormalizeAll(tags));
}
