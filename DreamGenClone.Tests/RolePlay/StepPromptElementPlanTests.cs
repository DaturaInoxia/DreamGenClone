using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The prompt-element plan is the deterministic half of "the prompt must not describe what an image supplies".
/// Its keys are the REAL compiled-brief payload properties, so the same fixtures the applier uses are the
/// authority here: <c>frozenState.location</c> / <c>frozenState.environment</c> exist, characters carry
/// <c>clothing</c> / <c>position</c>, and <c>character:{key}.appearance</c> is the applier's special-cased
/// appearance override.
/// </summary>
public sealed class StepPromptElementPlanTests
{
    /// <summary>
    /// A bound location removes the room's prose AND the lighting, because the image carries both. Lighting is the one
    /// that was measured: the pre-processor is required to give lighting its own sentence, so a bound location image
    /// was always fighting a prompt that had already decided the light. Reported live 2026-10-03 — the brief read
    /// "Thinning blue light from the last of the day; dim inside the shed.", the prompt transcribed it verbatim, and
    /// the render came out at mean brightness 43.5 against its reference's 72.0 without reproducing the room.
    ///
    /// <para>
    /// TIME OF DAY is deliberately NOT removed: it is a narrative anchor ("the last of the day"), not a visual claim
    /// the image duplicates — and mood stays for the same reason.
    /// </para>
    /// </summary>
    [Fact]
    public void ScopesFor_Location_RemovesLocationEnvironmentAndLightingProse()
    {
        var scopes = StepPromptElementPlan.ScopesFor(ImageStepSlotKind.Location, null);

        Assert.Equal(["frozenState.location", "frozenState.environment", "frozenState.lighting"], scopes);
        Assert.DoesNotContain("frozenState.timeOfDay", scopes);
        Assert.DoesNotContain("frozenState.mood", scopes);
    }

    [Fact]
    public void ScopesFor_Face_RemovesTheCharactersAppearanceLine()
    {
        var scopes = StepPromptElementPlan.ScopesFor(ImageStepSlotKind.Face, "p-becky");

        Assert.Equal(["character:p-becky.appearance"], scopes);
    }

    [Fact]
    public void ScopesFor_Wardrobe_RemovesTheClothingProse()
    {
        var scopes = StepPromptElementPlan.ScopesFor(ImageStepSlotKind.Wardrobe, "p-dean");

        Assert.Equal(["character:p-dean.clothing"], scopes);
    }

    [Fact]
    public void ScopesFor_PoseWithACharacter_RemovesThatCharactersStance()
    {
        var scopes = StepPromptElementPlan.ScopesFor(ImageStepSlotKind.Pose, "p-becky");

        Assert.Equal(["character:p-becky.position"], scopes);
    }

    /// <summary>
    /// A frame-wide pose has no character whose position could be cleared, so it addresses the Moment's visible
    /// action instead. Without this the scope would be the literal <c>character:.position</c> - a key that names
    /// nobody and would silently remove nothing.
    /// </summary>
    [Fact]
    public void ScopesFor_PoseWithoutACharacter_RemovesTheMomentsVisibleAction()
    {
        var scopes = StepPromptElementPlan.ScopesFor(ImageStepSlotKind.Pose, null);

        Assert.Equal(["moment.visibleAction"], scopes);
    }

    [Fact]
    public void ScopesFor_CharacterPose_SubsumesAppearanceWardrobeAndStance()
    {
        var scopes = StepPromptElementPlan.ScopesFor(ImageStepSlotKind.CharacterPose, "p-becky");

        Assert.Equal(
            ["character:p-becky.appearance", "character:p-becky.clothing", "character:p-becky.position"],
            scopes);
    }

    [Fact]
    public void ScopesFor_PerCharacterSlotWithoutAKey_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => StepPromptElementPlan.ScopesFor(ImageStepSlotKind.Face, null));

        Assert.Contains("profile key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScopesFor_SceneSlotWithAKey_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => StepPromptElementPlan.ScopesFor(ImageStepSlotKind.Location, "p-becky"));

        Assert.Contains("scene-level", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Binding a face AND a body for one character means the same thing twice - the appearance prose is supplied by
    /// images either way - so the plan must clear it ONCE. Duplicates would otherwise be applied twice for no gain.
    /// </summary>
    [Fact]
    public void Build_FaceAndBodyForOneCharacter_ClearsTheAppearanceLineOnce()
    {
        var scopes = StepPromptElementPlan.Build(
        [
            (ImageStepSlotKind.Face, "p-becky"),
            (ImageStepSlotKind.Body, "p-becky")
        ]);

        Assert.Equal(["character:p-becky.appearance"], scopes);
    }

    [Fact]
    public void Build_MixedSlots_KeepsDeclarationOrderAndDeduplicates()
    {
        var scopes = StepPromptElementPlan.Build(
        [
            (ImageStepSlotKind.CharacterPose, "p-becky"),
            (ImageStepSlotKind.Face, "p-becky"),
            (ImageStepSlotKind.Location, (string?)null),
            (ImageStepSlotKind.Wardrobe, "p-dean")
        ]);

        Assert.Equal(
            [
                "character:p-becky.appearance",
                "character:p-becky.clothing",
                "character:p-becky.position",
                "frozenState.location",
                "frozenState.environment",
                "frozenState.lighting",
                "character:p-dean.clothing"
            ],
            scopes);
    }

    [Fact]
    public void Build_NoFilledSlots_RemovesNothing()
    {
        var scopes = StepPromptElementPlan.Build([]);

        // A fully-textual step: every element keeps its prose, which is the other end of the same control.
        Assert.Empty(scopes);
    }

    /// <summary>
    /// The plan is only useful if every scope it emits is one the applier accepts - an unrecognised scope throws
    /// there, so a typo would surface as a failed prompt generation rather than a wrong prompt.
    /// </summary>
    [Fact]
    public void Scopes_AllUseAPrefixTheApplierAccepts()
    {
        var accepted = new[] { "scene", "moment", "frozenState", "continuity", "character" };

        foreach (var slotKind in Enum.GetValues<ImageStepSlotKind>())
        {
            var requirement = ImageStepSlotBlueprint.ActorKeyRequirementFor(slotKind);
            var actor = requirement == ImageStepActorKeyRequirement.Required || requirement == ImageStepActorKeyRequirement.Optional
                ? "p-probe"
                : null;
            if (requirement == ImageStepActorKeyRequirement.Forbidden)
            {
                actor = null;
            }

            foreach (var scope in StepPromptElementPlan.ScopesFor(slotKind, actor))
            {
                // The character scope is `character:{key}.{property}`, so its prefix is `character` and NOT the
                // text before the first dot - which would be `character:p-becky`.
                var prefix = scope.StartsWith(StepPromptElementPlan.CharacterScopePrefix, StringComparison.Ordinal)
                    ? "character"
                    : scope[..scope.IndexOf('.', StringComparison.Ordinal)];
                Assert.Contains(prefix, accepted);
            }
        }
    }
}
