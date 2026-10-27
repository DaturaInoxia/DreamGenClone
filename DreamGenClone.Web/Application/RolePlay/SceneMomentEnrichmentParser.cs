using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneMomentEnrichmentParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly string[] SequentialStateMarkers =
    [
        " then ", " followed by ", " transitions to ", " moves from ", " and then "
    ];

    private static readonly Regex TemporalActionPhrase = new(
        @"\b(?:before|after)\s+(?:(?:the|a|an|this|that|she|he|they|it|we|i|becky|dean)\s+)?[a-z][a-z'-]*ing\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public SceneMomentEnrichmentData Parse(
        string rawResponse,
        SceneMomentEnrichmentSourceSnapshot snapshot)
    {
        Require(rawResponse, "Moment enrichment response");
        ArgumentNullException.ThrowIfNull(snapshot);

        Response response;
        try
        {
            response = JsonSerializer.Deserialize<Response>(rawResponse, JsonOptions)
                ?? throw new InvalidOperationException("Moment enrichment response was null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Moment enrichment returned malformed or contract-invalid JSON.", ex);
        }

        if (response.SchemaVersion != SceneMomentEnrichmentContract.CurrentSchemaVersion)
            throw new InvalidOperationException($"Moment enrichment returned unsupported schemaVersion {response.SchemaVersion}.");
        if (!string.Equals(response.CatalogueBeatId, snapshot.BeatId, StringComparison.Ordinal))
            throw new InvalidOperationException("Moment enrichment response does not match the selected Beat.");
        if (!string.Equals(response.MomentId, snapshot.Moment.MomentId, StringComparison.Ordinal))
            throw new InvalidOperationException("Moment enrichment response does not match the selected Moment.");

        Require(response.VisualDescription, "Moment visual description");
        Require(response.Location, "Moment location");
        Require(response.TimeOfDay, "Moment time of day");
        Require(response.Lighting, "Moment lighting");
        Require(response.Environment, "Moment environment");
        Require(response.Mood, "Moment mood");
        ValidateFrozenText(response.VisualDescription, "visual description");
        Require(response.Event.Anchor, "Moment event anchor");
        Require(response.Event.Line, "Moment event line");
        ValidateFrozenText(response.Event.Line, "event line");

        RequireUnique(response.Characters.Select(item => item.ProfileKey), "Moment enrichment profile keys");
        var expectedProfiles = snapshot.Moment.Participants.ToDictionary(item => item.ProfileKey, StringComparer.Ordinal);
        var unknownProfiles = response.Characters
            .Where(item => !expectedProfiles.ContainsKey(item.ProfileKey))
            .Select(item => item.ProfileKey)
            .ToList();
        if (unknownProfiles.Count > 0)
            throw new InvalidOperationException($"Moment enrichment references unknown or non-cast profile keys: {string.Join(", ", unknownProfiles)}.");
        var missingProfiles = expectedProfiles.Keys
            .Where(key => response.Characters.All(item => !string.Equals(item.ProfileKey, key, StringComparison.Ordinal)))
            .ToList();
        if (missingProfiles.Count > 0)
            throw new InvalidOperationException($"Moment enrichment is missing selected-Moment participants: {string.Join(", ", missingProfiles)}.");

        var castNames = expectedProfiles.Values.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var namedInPlace = castNames
            .Where(name => Regex.IsMatch(
                response.Location,
                $@"\b{Regex.Escape(name)}\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToList();
        if (namedInPlace.Count > 0)
            throw new InvalidOperationException(
                $"Moment enrichment location must be a place only, but it names cast members: {string.Join(", ", namedInPlace)}. "
                + "Record cast position, posture and action on the character entries and the event line.");
        var characters = response.Characters.Select(item =>
        {
            var profile = expectedProfiles[item.ProfileKey];
            if (!string.Equals(item.Name, profile.Name, StringComparison.Ordinal))
                throw new InvalidOperationException($"Moment enrichment profile '{item.ProfileKey}' name does not match authoritative name '{profile.Name}'.");
            if (!string.Equals(item.Involvement, profile.Involvement, StringComparison.Ordinal))
                throw new InvalidOperationException($"Moment enrichment profile '{item.ProfileKey}' involvement does not match the selected Moment.");
            Require(item.PhysicalLocation, $"Character '{item.Name}' physical location");
            Require(item.Position, $"Character '{item.Name}' position");
            Require(item.ActionOrObservation, $"Character '{item.Name}' action or observation");
            Require(item.Sightline, $"Character '{item.Name}' sightline");
            Require(item.Clothing, $"Character '{item.Name}' clothing");
            var wardrobe = NormalizeWardrobe(item.Clothing, item.Name);
            Require(item.At.Anchor, $"Character '{item.Name}' at.anchor");
            Require(item.At.Posture, $"Character '{item.Name}' at.posture");
            Require(item.Facing.Toward, $"Character '{item.Name}' facing.toward");
            ValidateFrozenText(item.ActionOrObservation, $"character '{item.Name}' action or observation");
            RequireUnique(item.VisibleCharacterNames, $"Character '{item.Name}' visible character names", allowEmpty: true);
            var unknownNames = item.VisibleCharacterNames.Where(name => !castNames.Contains(name)).ToList();
            if (unknownNames.Count > 0)
                throw new InvalidOperationException($"Character '{item.Name}' references unknown visible character names: {string.Join(", ", unknownNames)}.");
            return new SceneMomentFrozenCharacter(
                profile.ProfileKey,
                profile.CharacterId,
                profile.Name,
                profile.Involvement,
                item.PhysicalLocation,
                item.Position,
                item.ActionOrObservation,
                item.Sightline,
                item.VisibleCharacterNames.ToArray(),
                wardrobe,
                At: new SceneMomentBodyPosition(item.At.Anchor.Trim(), item.At.Posture.Trim()),
                Facing: new SceneMomentFacing(NormalizeFacingTarget(item.Facing.Toward, expectedProfiles)));
        }).ToArray();

        var contacts = ValidateContacts(response.Contacts, expectedProfiles);

        RequireUnique(response.Objects, "Moment enrichment objects", allowEmpty: true);
        RequireUnique(response.InstantaneousSoundCueKeys, "Moment enrichment sound cue keys", allowEmpty: true);
        var knownCues = snapshot.SoundCues.ToDictionary(item => item.CueKey, StringComparer.Ordinal);
        var unknownCues = response.InstantaneousSoundCueKeys.Where(key => !knownCues.ContainsKey(key)).ToList();
        if (unknownCues.Count > 0)
            throw new InvalidOperationException($"Moment enrichment references unknown sound cue keys: {string.Join(", ", unknownCues)}.");
        var isSoundAnchor = snapshot.Moment.ProductionRoles.Contains(
            SceneMomentProductionRole.SoundEventAnchor.ToString(),
            StringComparer.Ordinal);
        if (!isSoundAnchor && response.InstantaneousSoundCueKeys.Count > 0)
            throw new InvalidOperationException("Moment enrichment cannot attach instantaneous sound cues to a Moment without SoundEventAnchor.");
        if (isSoundAnchor && response.InstantaneousSoundCueKeys.Count == 0)
            throw new InvalidOperationException("A SoundEventAnchor Moment requires at least one instantaneous sound cue.");
        var sounds = response.InstantaneousSoundCueKeys.Select(key =>
        {
            var cue = knownCues[key];
            var eventKey = cue.EventKey ?? cue.StartEventKey ?? cue.EndEventKey;
            if (string.IsNullOrWhiteSpace(eventKey))
                throw new InvalidOperationException($"Sound cue '{key}' has no event anchor for an instantaneous Moment.");
            return new SceneMomentInstantaneousSoundEvent(key, eventKey, cue.Description);
        }).ToArray();

        RequireUnique(response.VideoKeyState.Roles, "Moment enrichment video roles", allowEmpty: true);
        var expectedVideoRoles = snapshot.Moment.ProductionRoles
            .Where(role => role is nameof(SceneMomentProductionRole.VideoStart)
                or nameof(SceneMomentProductionRole.VideoEnd)
                or nameof(SceneMomentProductionRole.VideoInternalKeyframe))
            .ToHashSet(StringComparer.Ordinal);
        if (!expectedVideoRoles.SetEquals(response.VideoKeyState.Roles))
            throw new InvalidOperationException("Moment enrichment video roles must exactly match the selected Moment video roles.");
        if (response.VideoKeyState.StateChangeAllowed)
            throw new InvalidOperationException("Moment enrichment video key state cannot allow state change.");

        var frozen = new SceneMomentFrozenStateContract(
            response.VisualDescription,
            characters,
            response.Location,
            response.TimeOfDay,
            response.Lighting,
            response.Environment,
            response.Mood,
            response.Objects.ToArray(),
            snapshot.Moment.FrozenState,
            contacts,
            new SceneMomentEvent(response.Event.Anchor.Trim(), response.Event.Line.Trim()));
        var video = new SceneMomentVideoKeyState(response.VideoKeyState.Roles.ToArray(), false);
        return new SceneMomentEnrichmentData(
            JsonSerializer.Serialize(frozen, JsonOptions),
            JsonSerializer.Serialize(sounds, JsonOptions),
            JsonSerializer.Serialize(video, JsonOptions));
    }

    private static void ValidateFrozenText(string value, string field)
    {
        var normalized = $" {value.Trim().ToLowerInvariant()} ";
        if (SequentialStateMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal))
            || TemporalActionPhrase.IsMatch(value))
            throw new InvalidOperationException($"Moment enrichment {field} describes sequential before/after/then action instead of one frozen state.");
    }

    /// <summary>
    /// Canonicalizes a recorded facing target to the participant PROFILE KEY when it names a cast member.
    /// </summary>
    /// <remarks>
    /// The contract asks for a profile key, but the model can return it inside a phrase ("toward p0", "facing Becky").
    /// Stored verbatim, that phrase becomes a world fact the prompt prints, and the caption writer copies the raw key
    /// into the image prompt — where a diffusion model cannot resolve it (and "(p0)" is weight syntax). The key is the
    /// one canonical form the projection can compare exactly, so resolve it here; a place, body area or direction that
    /// names no cast member is left exactly as written.
    /// </remarks>
    internal static string NormalizeFacingTarget(
        string toward,
        IReadOnlyDictionary<string, SceneMomentEnrichmentParticipantSnapshot> profiles)
    {
        var trimmed = toward.Trim();
        var candidate = trimmed;
        foreach (var prefix in FacingPrefixes)
        {
            if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate[prefix.Length..].Trim();
                break;
            }
        }

        if (candidate.Length == 0) return trimmed;
        foreach (var profile in profiles.Values)
        {
            if (string.Equals(profile.ProfileKey, candidate, StringComparison.OrdinalIgnoreCase)
                || string.Equals(profile.Name, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return profile.ProfileKey;
            }
        }

        return trimmed;
    }

    /// <summary>Leading words a facing target may carry before the thing it names; never part of the target itself.</summary>
    private static readonly string[] FacingPrefixes =
        ["facing toward ", "facing towards ", "towards ", "toward ", "facing ", "to ", "at "];

    /// <summary>
    /// Keeps a wardrobe statement AFFIRMATIVE, removing any clause that names a garment the person is NOT wearing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Naked; no tank or cutoffs on." is the reported defect (2026-10-24). An image model renders what a prompt names,
    /// so naming a garment that is absent is worse than saying nothing at all: "no tank" puts a tank in the picture.
    /// The wardrobe fact is what the person IS wearing (or that they are nude/bare), and that affirmative part is
    /// already in the record — the negated one is pure noise with a harmful side effect.
    /// </para>
    /// <para>
    /// This REMOVES text; it never adds or rewrites one, so nothing is invented (the affirmative clause carries the
    /// meaning, and a bare "nude" is unaffected). A wardrobe value that is ONLY a negation carries no usable fact, so it
    /// is refused by name — a re-enrichment can state what the body is actually wearing, whereas guessing between "nude"
    /// and "bare-chested" would be an invented fact.
    /// </para>
    /// </remarks>
    internal static string NormalizeWardrobe(string clothing, string characterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clothing);

        var cleaned = clothing;
        foreach (var pattern in GarmentNegations)
        {
            cleaned = pattern.Replace(cleaned, string.Empty);
        }

        cleaned = TidyWardrobeSeparators(cleaned);
        if (cleaned.Length == 0)
        {
            throw new InvalidOperationException(
                $"Character '{characterName}' clothing records only what they are NOT wearing ('{clothing.Trim()}'). "
                + "State the wardrobe affirmatively - what the body is wearing, or that it is nude/bare - because a "
                + "negated garment still puts that garment in the picture.");
        }

        return cleaned;
    }

    /// <summary>
    /// Phrases that name an absent garment. Each one is removed WITH its object ("no tank", "without any clothing"),
    /// leaving the affirmative remainder of the same clause intact. The specific verbs run FIRST: a generic "no" that
    /// fired inside "has no shirt" would leave the verb stranded ("She has , jeans").
    /// </summary>
    private static readonly Regex[] GarmentNegations =
    [
        new(@"\b(?:doesn't|don't|didn't|does\s+not|do\s+not)\s+(?:have|wear)\s+[^,;.]+?(?=(?:,|;|\.|$))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
        new(@"\b(?:has|have|had)\s+no\s+[^,;.]+?(?=(?:,|;|\.|$))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
        new(@"\b(?:is|are|was|were)\s+not\s+wearing\s+[^,;.]+?(?=(?:,|;|\.|$))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
        new(@"\bnot\s+(?:currently\s+)?wearing\s+[^,;.]+?(?=(?:,|;|\.|$))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
        new(@"\bwithout\s+(?:any\s+)?[^,;.]+?(?=(?:,|;|\.|$))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
        new(@"\bstripped\s+of\s+[^,;.]+?(?=(?:,|;|\.|$))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
        new(@"(?<!\b(?:has|have|had|is|are|was|were|does|do|did)\s)\b(?:and\s+)?(?:but\s+)?no\s+(?!(?:longer|one)\b)[^,;.]+?(?=(?:,|;|\.|$))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)
    ];

    /// <summary>Removes the separators a stripped clause leaves behind, and capitalises what now starts the value.</summary>
    private static string TidyWardrobeSeparators(string value)
    {
        var tidied = Regex.Replace(value, @"\s{2,}", " ").Trim();
        tidied = Regex.Replace(tidied, @"[,;]\s*(?=[,;.])", string.Empty);
        tidied = Regex.Replace(tidied, @"\b(?:and|or)\s*(?=,|;|\.|$)", string.Empty, RegexOptions.IgnoreCase);
        tidied = Regex.Replace(tidied, @"\s{2,}", " ").Trim();
        // A wardrobe value is a fragment, not a sentence: a separator left dangling by a removal is not kept.
        tidied = tidied.Trim(',', ';', '-', '–', '.', ' ');
        if (tidied.Length == 0)
        {
            return string.Empty;
        }

        return char.ToUpperInvariant(tidied[0]) + tidied[1..];
    }

    private static void RequireUnique(IEnumerable<string> values, string source, bool allowEmpty = false)
    {
        var list = values.ToList();
        if ((!allowEmpty && list.Count == 0)
            || list.Any(string.IsNullOrWhiteSpace)
            || list.Distinct(StringComparer.Ordinal).Count() != list.Count)
            throw new InvalidOperationException($"{source} must be non-empty and unique.");
    }

    private static void Require(string? value, string source)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{source} is required.");
    }

    /// <summary>
    /// Validates contacts STRUCTURALLY: both ends must be selected cast profile keys and every naming field must be
    /// present. Nothing is inferred — an empty list is a valid answer for an instant in which two bodies do not touch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A contact whose two ends are the SAME profile key is a self-contact and is ACCEPTED: a hand on one's own thigh,
    /// one's own hands clasped, one's own hand on one's own body is a real fact of the instant, and it is the only way
    /// a first-person frame learns about the camera holder's own body. Refusing it made a legitimate enrichment fail
    /// outright (reported live 2026-10-24: "contact 0 pairs 'p0' with itself"), which turned a valid record into a
    /// blocked render.
    /// </para>
    /// <para>
    /// A contact repeated verbatim is the same fact stated twice, so the FIRST occurrence wins. Redundancy in model
    /// output is not a configuration error — it carries no extra meaning — and failing the whole enrichment over it
    /// would block a render for nothing.
    /// </para>
    /// </remarks>
    private static SceneMomentContact[] ValidateContacts(
        IReadOnlyList<ContactInput> contacts,
        IReadOnlyDictionary<string, SceneMomentEnrichmentParticipantSnapshot> expectedProfiles)
    {
        var result = new List<SceneMomentContact>(contacts.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < contacts.Count; index++)
        {
            var contact = contacts[index];
            if (!expectedProfiles.ContainsKey(contact.From))
                throw new InvalidOperationException($"Moment enrichment contact {index} references unknown profile key '{contact.From}'.");
            if (!expectedProfiles.ContainsKey(contact.To))
                throw new InvalidOperationException($"Moment enrichment contact {index} references unknown profile key '{contact.To}'.");
            Require(contact.Part, $"Contact {index} part");
            Require(contact.TargetPart, $"Contact {index} target part");
            Require(contact.Manner, $"Contact {index} manner");
            if (!seen.Add($"{contact.From}|{contact.Part}|{contact.To}|{contact.TargetPart}")) continue;
            result.Add(new SceneMomentContact(
                contact.From,
                contact.Part.Trim(),
                contact.To,
                contact.TargetPart.Trim(),
                contact.Manner.Trim()));
        }

        return [.. result];
    }

    private sealed class Response
    {
        public required int SchemaVersion { get; init; }
        public required string CatalogueBeatId { get; init; }
        public required string MomentId { get; init; }
        public required string VisualDescription { get; init; }
        public required List<CharacterInput> Characters { get; init; }
        public required string Location { get; init; }
        public required string TimeOfDay { get; init; }
        public required string Lighting { get; init; }
        public required string Environment { get; init; }
        public required string Mood { get; init; }
        public required List<string> Objects { get; init; }
        public required EventInput Event { get; init; }
        public required List<ContactInput> Contacts { get; init; }
        public required List<string> InstantaneousSoundCueKeys { get; init; }
        public required VideoKeyStateInput VideoKeyState { get; init; }
    }

    private sealed class CharacterInput
    {
        public required string Name { get; init; }
        public required string ProfileKey { get; init; }
        public required string Involvement { get; init; }
        public required string PhysicalLocation { get; init; }
        public required string Position { get; init; }
        public required string ActionOrObservation { get; init; }
        public required string Sightline { get; init; }
        public required List<string> VisibleCharacterNames { get; init; }
        public required string Clothing { get; init; }
        public required BodyPositionInput At { get; init; }
        public required FacingInput Facing { get; init; }
    }

    private sealed class BodyPositionInput
    {
        public required string Anchor { get; init; }
        public required string Posture { get; init; }
    }

    private sealed class FacingInput
    {
        public required string Toward { get; init; }
    }

    private sealed class EventInput
    {
        public required string Anchor { get; init; }
        public required string Line { get; init; }
    }

    private sealed class ContactInput
    {
        public required string From { get; init; }
        public required string Part { get; init; }
        public required string To { get; init; }
        public required string TargetPart { get; init; }
        public required string Manner { get; init; }
    }

    private sealed class VideoKeyStateInput
    {
        public required List<string> Roles { get; init; }
        public required bool StateChangeAllowed { get; init; }
    }
}