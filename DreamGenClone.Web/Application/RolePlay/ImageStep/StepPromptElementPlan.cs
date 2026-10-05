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
            // The room is in the image, so neither the location prose nor the environment prose needs to be written.
            //
            // LIGHTING is removed for the same reason and it is the one that was measured: the pre-processor is
            // REQUIRED to give lighting its own sentence (SceneImageCompilerSystemPrompts), so a bound location image
            // was always fighting a prompt that had already decided the light. Reported live 2026-10-03 — the brief
            // read "Thinning blue light from the last of the day; dim inside the shed." and the prompt transcribed it
            // verbatim, so the render came out at mean brightness 43.5 against its reference's 72.0 and did not
            // reproduce the room. The role clause already promises the model that "the room supplies the setting and
            // its own lighting"; this is what makes that promise true.
            //
            // TIME OF DAY stays: it is a narrative anchor ("the last of the day"), not a visual claim the image
            // duplicates. Mood stays too — it makes no claim about what the image owns.
            ImageStepSlotKind.Location => ["frozenState.location", "frozenState.environment", "frozenState.lighting"],

            // The appearance line is what an identity/body image supplies. Both kinds clear it; the caller
            // de-duplicates, so binding face AND body clears it once.
            ImageStepSlotKind.Face => [$"{actor}.appearance"],
            ImageStepSlotKind.Body => [$"{actor}.appearance"],

            ImageStepSlotKind.Wardrobe => [$"{actor}.clothing"],

            // A skeleton supplies the stance. Addressed at the character when one is named, and at the Moment's
            // visible action otherwise - a frame-wide pose has no character whose position could be cleared.
            ImageStepSlotKind.Pose => namesActor ? [$"{actor}.position"] : ["moment.visibleAction"],

            // A character pose asset carries appearance AND wardrobe AND stance in one image.
            ImageStepSlotKind.CharacterPose =>
                [$"{actor}.appearance", $"{actor}.clothing", $"{actor}.position"],

            _ => throw new InvalidOperationException(
                $"Reference slot kind '{slotKind}' has no prompt-element mapping. Add one deliberately: silently "
                + "mapping it to nothing would leave the prompt describing an element an image already supplies.")
        };
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
}
