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
    public void ScopesFor_Location_RemovesEveryRoomCarryingElementButKeepsTimeAndMood()
    {
        var scopes = StepPromptElementPlan.ScopesFor(ImageStepSlotKind.Location, null);

        Assert.Equal(
            [
                "frozenState.location",
                "frozenState.objects", "moment.compositionRationale"
            ],
            scopes);
        Assert.DoesNotContain("frozenState.timeOfDay", scopes);
        Assert.DoesNotContain("frozenState.mood", scopes);
        // The PEOPLE are never taken from the text: only the room is the image's.
        Assert.DoesNotContain("frozenState.event.line", scopes);
        Assert.DoesNotContain("frozenState.characters", scopes);
        // B-167 D2: the duplicate prose restatements and the beat continuity blocks are not selected into a Still
        // brief at all, so "remove the room" addresses only leaves the brief carries.
        Assert.DoesNotContain(scopes, scope => scope.StartsWith("continuity.", StringComparison.Ordinal));
        Assert.DoesNotContain("frozenState.visualDescription", scopes);
        Assert.DoesNotContain("frozenState.continuityState", scopes);
    }

    [Fact]
    public void ScopesFor_Face_RemovesTheFaceButKeepsTheBuild()
    {
        // B-166 D10: a face reference supplies the FACE and says nothing about the build, so a face-only binding must
        // not delete the build prose.
        var scopes = StepPromptElementPlan.ScopesFor(ImageStepSlotKind.Face, "p-becky");

        Assert.Equal(["character:p-becky.appearance.face"], scopes);
    }

    [Fact]
    public void ScopesFor_Body_RemovesTheFaceAndTheBuild()
    {
        // A body reference IS the character: it shows the build and the face together.
        var scopes = StepPromptElementPlan.ScopesFor(ImageStepSlotKind.Body, "p-becky");

        Assert.Equal(
            ["character:p-becky.appearance.face", "character:p-becky.appearance.build"],
            scopes);
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
    /// A frame-wide pose has no character whose position could be cleared, so it addresses the Moment's single event
    /// sentence instead. Without this the scope would be the literal <c>character:.position</c> - a key that names
    /// nobody and would silently remove nothing.
    /// </summary>
    [Fact]
    public void ScopesFor_PoseWithoutACharacter_RemovesTheEventLine()
    {
        var scopes = StepPromptElementPlan.ScopesFor(ImageStepSlotKind.Pose, null);

        Assert.Equal(["frozenState.event.line"], scopes);
    }

    [Fact]
    public void ScopesFor_CharacterPose_SubsumesAppearanceWardrobeAndStance()
    {
        var scopes = StepPromptElementPlan.ScopesFor(ImageStepSlotKind.CharacterPose, "p-becky");

        Assert.Equal(
            [
                "character:p-becky.appearance.face",
                "character:p-becky.appearance.build",
                "character:p-becky.clothing",
                "character:p-becky.position"
            ],
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
    /// Binding a face AND a body for one character means the face is cleared once (both supply it) and the build once
    /// (only the body supplies it). Duplicates would be applied twice for no gain (B-166 D10).
    /// </summary>
    [Fact]
    public void Build_FaceAndBodyForOneCharacter_ClearsEachAppearanceElementOnce()
    {
        var scopes = StepPromptElementPlan.Build(
        [
            (ImageStepSlotKind.Face, "p-becky"),
            (ImageStepSlotKind.Body, "p-becky")
        ]);

        Assert.Equal(["character:p-becky.appearance.face", "character:p-becky.appearance.build"], scopes);
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
                "character:p-becky.appearance.face",
                "character:p-becky.appearance.build",
                "character:p-becky.clothing",
                "character:p-becky.position",
                "frozenState.location",
                "frozenState.objects",
                "moment.compositionRationale",
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

    /// <summary>
    /// Every scope the plan can emit must also be NAMED, because the User Intent path has no brief to strip — its only
    /// protection is telling the compiler which elements the images own. A scope with no name would be one an intent
    /// draft silently describes itself, which is how an invented fence-and-posts setting overrode a bound location
    /// plate (SceneImage 2ecd4862, 2026-10-17).
    /// </summary>
    [Fact]
    public void Describe_NamesEveryScopeThePlanCanEmit()
    {
        var described = new List<string>();

        foreach (var slotKind in Enum.GetValues<ImageStepSlotKind>())
        {
            var requirement = ImageStepSlotBlueprint.ActorKeyRequirementFor(slotKind);
            var actor = requirement == ImageStepActorKeyRequirement.Required || requirement == ImageStepActorKeyRequirement.Optional
                ? "p-probe"
                : null;

            foreach (var scope in StepPromptElementPlan.ScopesFor(slotKind, actor))
            {
                var text = StepPromptElementPlan.Describe(scope);

                Assert.False(string.IsNullOrWhiteSpace(text));
                Assert.DoesNotContain("prompt element", text, StringComparison.OrdinalIgnoreCase);
                described.Add(text);
            }
        }

        // A per-character scope is addressed to the character, so its name is too.
        Assert.Contains("p-probe's clothing", described);
        Assert.Contains("p-probe's appearance — face, features, hair and skin tone", described);
        Assert.Contains("p-probe's build — height, body shape and proportions", described);

        // A scene-level scope belongs to nobody, so it carries no owner prefix.
        Assert.Contains("the setting — its structure, surfaces and objects", described);
        // LIGHTING is no longer removed for a location reference (operator decision 2026-10-25), so it is not one of the
        // scopes the plan can emit — it stays an editable Prompt Input row.
        Assert.DoesNotContain("the lighting", described);
    }

    [Fact]
    public void Describe_UnknownScope_FailsFastRatherThanPassingItOverInSilence()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => StepPromptElementPlan.Describe("frozenState.somethingNew"));

        Assert.Contains("has no readable description", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every scope must also carry an OWNERSHIP rule, because the two halves are not the same: a setting an image owns
    /// must stay out of the prose, while an outfit an image owns has to be named and put on the figure. A blanket
    /// "do not describe" produced a nude render (SceneImage c0d77b66, 2026-10-18).
    /// </summary>
    [Fact]
    public void Ownership_EveryScopeResolvesAndClothingIsTheOnlyWornElement()
    {
        var worn = new List<string>();

        foreach (var slotKind in Enum.GetValues<ImageStepSlotKind>())
        {
            var requirement = ImageStepSlotBlueprint.ActorKeyRequirementFor(slotKind);
            var actor = requirement == ImageStepActorKeyRequirement.Required || requirement == ImageStepActorKeyRequirement.Optional
                ? "p-probe"
                : null;

            foreach (var scope in StepPromptElementPlan.ScopesFor(slotKind, actor))
            {
                var ownership = StepPromptElementPlan.OwnershipOf(scope);
                if (ownership == StepPromptElementPlan.PromptElementOwnership.SuppliedWorn)
                {
                    worn.Add(scope);
                }
            }
        }

        Assert.Single(worn.Distinct(StringComparer.Ordinal));
        Assert.Equal("character:p-probe.clothing", worn[0]);
        Assert.Equal(
            StepPromptElementPlan.PromptElementOwnership.SuppliedSilently,
            StepPromptElementPlan.OwnershipOf("frozenState.location"));
    }

    [Fact]
    public void Ownership_UnknownScope_FailsFast()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => StepPromptElementPlan.OwnershipOf("frozenState.somethingNew"));

        Assert.Contains("has no ownership rule", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A prop replaces NO brief element, deliberately: the only candidate is the whole <c>scene.objects</c> list, so
    /// removing it because one prop is bound would delete every unrelated object the brief describes. The slot is still
    /// supported, and asking for its scopes with or without an actor must not throw.
    /// </summary>
    [Fact]
    public void Scopes_PropReplacesNoBriefElement()
    {
        Assert.Empty(StepPromptElementPlan.ScopesFor(ImageStepSlotKind.Prop, null));
        Assert.Empty(StepPromptElementPlan.ScopesFor(ImageStepSlotKind.Prop, "p-probe"));
    }

    [Fact]
    public void Describe_BlankScope_FailsFast()
    {
        Assert.Throws<InvalidOperationException>(() => StepPromptElementPlan.Describe("  "));
    }
}
