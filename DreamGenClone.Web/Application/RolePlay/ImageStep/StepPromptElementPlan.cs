using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// Which payload elements a filled reference slot REPLACES, expressed in the element keys
/// <see cref="ScenePromptOverridesApplier"/> already accepts (<c>scene.*</c>, <c>moment.*</c>,
/// <c>frozenState.*</c>, <c>continuity.&lt;block&gt;.*</c>, <c>character:{key}.*</c>).
/// </summary>
/// <remarks>
/// This is the deterministic half of the composer's prompt behaviour: an image supplies an element, so the prompt
/// must not describe it a second time. It is deliberately pure - no DB, no clock, no services - so the mapping can
/// be reviewed and tested on its own, and so the plan and the render always derive from the SAME slot set.
///
/// Two rules this encodes:
/// <list type="bullet">
/// <item><description>
/// The keys are the REAL payload properties (verified against the compiled-brief snapshot: characters carry
/// <c>clothing</c> / <c>position</c> / <c>physicalLocation</c>, and the snapshot carries <c>frozenState.location</c> /
/// <c>frozenState.environment</c>). Inventing a property name here would throw at apply time, which is why nothing
/// is guessed.
/// </description></item>
/// <item><description>
/// <c>character:{key}.appearance</c> is special-cased by the applier as an APPEARANCE override: setting it empty
/// REMOVES the appearance line. Face and Body both map to it because a face image and a body image each supply the
/// part of the appearance prose they depict - and the scopes are de-duplicated, so supplying both clears it once
/// rather than twice.
/// </description></item>
/// </list>
/// </remarks>
public static class StepPromptElementPlan
{
    /// <summary>The prefix <see cref="ScenePromptOverridesApplier"/> expects for a per-character element.</summary>
    public const string CharacterScopePrefix = "character:";

    /// <summary>
    /// The complete payload signature one filled LOCATION slot replaces. Exposed as a named list (rather than only as
    /// the <c>Location</c> switch arm) so the removal notice and the inspector's "remove the room" path can both say
    /// "the room" from the SAME set — two lists of the room's elements is how a binding and an operator edit come to
    /// mean different things by "remove the room".
    /// </summary>
    public static readonly IReadOnlyList<string> LocationScopes =
    [
        "frozenState.location",
        "frozenState.environment",
        "frozenState.lighting",
        "frozenState.objects",
        "frozenState.visualDescription",
        "frozenState.continuityState",
        "moment.frozenState",
        "moment.compositionRationale",
        "continuity.start.location",
        "continuity.start.objectStates",
        "continuity.start.lighting",
        "continuity.start.characterStates",
        "continuity.start.stateSummary",
        "continuity.end.location",
        "continuity.end.objectStates",
        "continuity.end.lighting",
        "continuity.end.characterStates",
        "continuity.end.stateSummary"
    ];

    /// <summary>
    /// The payload elements one filled slot replaces. Ordered as declared, so the resulting list is stable and
    /// readable rather than dependent on a hash set's iteration order.
    /// </summary>
    /// <param name="slotKind">The slot that is filled.</param>
    /// <param name="actorKey">
    /// The character's payload key for a per-character slot. <b>This must be the profile key</b> the snapshot uses
    /// (<c>character:{profileKey}.clothing</c>), not a scenario character id - the applier addresses characters by
    /// profile key, and an id would silently match nothing.
    /// </param>
    public static IReadOnlyList<string> ScopesFor(ImageStepSlotKind slotKind, string? actorKey)
    {
        var requirement = ImageStepSlotBlueprint.ActorKeyRequirementFor(slotKind);
        var namesActor = !string.IsNullOrWhiteSpace(actorKey);
        if (requirement == ImageStepActorKeyRequirement.Required && !namesActor)
        {
            throw new InvalidOperationException(
                $"Reference slot '{slotKind}' is per-character, so it cannot be mapped to prompt elements without the "
                + "character's profile key.");
        }

        if (requirement == ImageStepActorKeyRequirement.Forbidden && namesActor)
        {
            throw new InvalidOperationException(
                $"Reference slot '{slotKind}' is scene-level, so it does not address one character's elements. "
                + $"Remove actor key '{actorKey}'.");
        }

        var actor = $"{CharacterScopePrefix}{actorKey?.Trim()}";

        return slotKind switch
        {
            // The room is in the image, so NONE of the room's prose needs to be written — and "none" has to mean every
            // element that restates the place, not just the three obvious ones. The brief repeats the room in the
            // moment continuity line, the visual description, the global continuity state, the object list, the
            // composition rationale and BOTH continuity blocks (setting, object states, lighting, state summary).
            // Leaving those behind is how a bound location still produced a prompt that described "corrugated metal
            // walls, wooden floorboards showing cracks, and a pitched roof" while saying almost nothing about the
            // people in frame (reported live 2026-10-20). TIME OF DAY stays: it is a narrative anchor, not a visual
            // claim the image duplicates. Mood stays too — it makes no claim about what the image owns.
            ImageStepSlotKind.Location => LocationScopes,

            // B-166 D10: a FACE image supplies the face; a BODY image supplies the face AND the build (the body
            // reference IS the character). Mapping both to one `appearance` element deleted the build prose on a
            // face-only binding - the defect reported as "the characters are off a little".
            ImageStepSlotKind.Face => [$"{actor}.appearance.face"],
            ImageStepSlotKind.Body => [$"{actor}.appearance.face", $"{actor}.appearance.build"],

            ImageStepSlotKind.Wardrobe => [$"{actor}.clothing"],

            // A skeleton supplies the stance. Addressed at the character when one is named, and at the Moment's
            // visible action otherwise - a frame-wide pose has no character whose position could be cleared.
            ImageStepSlotKind.Pose => namesActor ? [$"{actor}.position"] : ["moment.visibleAction"],

            // A character pose asset carries appearance AND wardrobe AND stance in one image.
            ImageStepSlotKind.CharacterPose =>
                [$"{actor}.appearance.face", $"{actor}.appearance.build", $"{actor}.clothing", $"{actor}.position"],

            // A PROP replaces no brief element, and this empty list is a decision rather than an omission. The only
            // candidate is `scene.objects`, which is the WHOLE object list: removing it because one prop is bound would
            // delete every other object the brief describes. A prop is ADDITIVE — it is an object the operator is
            // adding to the frame, so nothing has to be taken away to make room for it. The intent path still NAMES it
            // and places it (see PromptElementOwnership.SuppliedPlaced), which is what stops the model ignoring it.
            ImageStepSlotKind.Prop => [],

            _ => throw new InvalidOperationException(
                $"Reference slot kind '{slotKind}' has no prompt-element mapping. Add one deliberately: silently "
                + "mapping it to nothing would leave the prompt describing an element an image already supplies.")
        };
    }

    /// <summary>
    /// How an element that an image supplies must be treated in prose — the ONE distinction the User Intent notice needs,
    /// kept beside the scope set for the same reason <see cref="Describe"/> is.
    /// </summary>
    public enum PromptElementOwnership
    {
        /// <summary>
        /// The image IS the element. Prose beside it is inexact, and writing any invites a version that competes with
        /// the image — a setting, an appearance, a stance.
        /// </summary>
        SuppliedSilently = 1,

        /// <summary>
        /// The image supplies the element but the element must still be PUT ON: a garment reference shows clothes, not
        /// a dressed figure, so staying silent about it dresses nobody. Measured both ways on 2026-10-17/18: naming the
        /// shorts produced shorts, and staying silent produced a nude render
        /// (SceneImage c0d77b66, from a blanket "do not describe" that applied to clothing too).
        /// </summary>
        SuppliedWorn = 2
    }

    /// <summary>
    /// Whether prose must stay silent about this scope or must name it and put it on the figure.
    /// </summary>
    public static PromptElementOwnership OwnershipOf(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new InvalidOperationException("A prompt element scope is required to decide how it is stated.");
        }

        var trimmed = scope.Trim();
        var field = AppearanceElementOf(trimmed) ?? LastSegment(trimmed);

        return field switch
        {
            // A wardrobe reference is a photograph of CLOTHES. Something has to be said for them to be worn.
            "clothing" => PromptElementOwnership.SuppliedWorn,

            "location" or "environment" or "lighting" or "visibleAction"
                or "appearance" or "appearance.face" or "appearance.build"
                or "position" or "objects" or "visualDescription"
                or "continuityState" or "frozenState" or "compositionRationale"
                or "objectStates" or "stateSummary" or "characterStates" => PromptElementOwnership.SuppliedSilently,

            _ => throw new InvalidOperationException(
                $"Prompt element '{scope}' has no ownership rule. Add one deliberately: an element an image supplies "
                + "must be either kept out of the prose or named as worn, and silence about that choice is what turned "
                + "a wardrobe reference into a nude render.")
        };
    }

    /// <summary>
    /// The two-part appearance element a scope names (<c>appearance.face</c> / <c>appearance.build</c>), or null.
    /// Kept here beside <see cref="OwnershipOf"/> so the element vocabulary has one owner: a last-segment read would
    /// see only "face" and could not tell an appearance trait from an unrelated field of the same name.
    /// </summary>
    private static string? AppearanceElementOf(string scope)
        => scope switch
        {
            _ when scope.EndsWith(".appearance.face", StringComparison.OrdinalIgnoreCase) => "appearance.face",
            _ when scope.EndsWith(".appearance.build", StringComparison.OrdinalIgnoreCase) => "appearance.build",
            _ => null
        };

    private static string LastSegment(string scope)
    {
        var separator = scope.LastIndexOf('.');
        return separator < 0 ? scope : scope[(separator + 1)..];
    }

    /// <summary>
    /// The de-duplicated element keys for a whole step, in the order the slots were supplied. Binding a face and a
    /// body for the same character yields ONE appearance entry, because the second would be the same removal.
    /// </summary>
    public static IReadOnlyList<string> Build(IEnumerable<(ImageStepSlotKind SlotKind, string? ActorKey)> filledSlots)
    {
        ArgumentNullException.ThrowIfNull(filledSlots);

        var scopes = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in filledSlots)
        {
            foreach (var scope in ScopesFor(slot.SlotKind, slot.ActorKey))
            {
                if (seen.Add(scope))
                {
                    scopes.Add(scope);
                }
            }
        }

        return scopes;
    }

    /// <summary>
    /// How one of these scopes is NAMED to the prompt compiler.
    /// </summary>
    /// <remarks>
    /// The scene path can stay silent about a supplied element: it REMOVES the element from the brief, so the compiler
    /// never sees it. A <c>User Intent</c> draft has no brief to strip — the compiler is handed the operator's own
    /// prose — so the only way to stop it writing that element is to SAY which elements the images own. That statement
    /// must come from this file, because this file is where the scope set is decided: a second table of element names
    /// would let the two drift, and an element the compiler is not told about is an element it describes anyway
    /// (measured on SceneImage 2ecd4862, 2026-10-17, where an invented fence-and-posts setting overrode the location
    /// reference). An unmapped scope throws rather than being passed over in silence.
    /// </remarks>
    public static string Describe(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new InvalidOperationException("A prompt element scope is required to name it to the compiler.");
        }

        var trimmed = scope.Trim();
        var appearanceElement = AppearanceElementOf(trimmed);
        var separator = trimmed.LastIndexOf('.');
        var field = appearanceElement ?? (separator < 0 ? trimmed : trimmed[(separator + 1)..]);
        var owner = appearanceElement is null
            ? (separator < 0 ? string.Empty : trimmed[..separator])
            : trimmed[..^(appearanceElement.Length + 1)];
        var actor = owner.StartsWith(CharacterScopePrefix, StringComparison.Ordinal)
            ? owner[CharacterScopePrefix.Length..].Trim()
            : null;

        var element = field switch
        {
            "location" => "the setting — its structure, surfaces and objects",
            "environment" => "the surroundings",
            "lighting" => "the lighting",
            "objects" => "the objects and props",
            "objectStates" => "the objects and props",
            "characterStates" => "the continuity character states",
            "visualDescription" => "the visual description",
            "continuityState" => "the global continuity state",
            "frozenState" => "the moment continuity line",
            "compositionRationale" => "the composition rationale",
            "stateSummary" => "the continuity summary",
            "visibleAction" => "the visible action",
            "appearance" => "appearance — face, hair, skin tone, build and proportions",
            "appearance.face" => "appearance — face, features, hair and skin tone",
            "appearance.build" => "build — height, body shape and proportions",
            "clothing" => "clothing",
            "position" => "stance and position",
            _ => throw new InvalidOperationException(
                $"Prompt element '{scope}' has no readable description. Add one deliberately: an element an image "
                + "supplies must be named to the compiler, not passed over in silence.")
        };

        return string.IsNullOrWhiteSpace(actor) ? element : $"{actor}'s {element}";
    }
}
