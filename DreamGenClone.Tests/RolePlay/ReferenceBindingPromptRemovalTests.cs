using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The join between a step's reference bindings and the generated prompt. The whole point is that an element
/// supplied as an image is not ALSO described in prose, and that the operator's own edits are never overruled by
/// that default.
/// </summary>
public sealed class ReferenceBindingPromptRemovalTests
{
    private static ReferenceApplicationSelection Binding(
        string elementKey,
        string strategy = "NativeMultiReference",
        string? kind = null,
        string? actorKey = null,
        bool withReference = true) => new()
    {
        ElementKey = elementKey,
        Kind = kind,
        ActorKey = actorKey,
        SemanticRole = "role",
        Strategy = strategy,
        SceneAssetId = withReference ? "asset-1" : null,
        SceneAssetImageId = withReference ? "image-1" : null,
        SceneAssetVersion = withReference ? 1 : null,
        SceneAssetSha256 = withReference ? "SHA" : null
    };

    /// <summary>
    /// A bound location removes the room's prose AND its lighting. Lighting is the one that was measured: the
    /// pre-processor is REQUIRED to give lighting its own sentence, so before this a bound location image was always
    /// fighting a prompt that had already decided the light (reported live 2026-10-03 — brief lighting
    /// "Thinning blue light from the last of the day; dim inside the shed." transcribed verbatim into the prompt, and
    /// the render landed at mean brightness 43.5 against its reference's 72.0 without reproducing the room).
    ///
    /// <para>
    /// TIME OF DAY and MOOD are deliberately NOT removed: time of day is a narrative anchor ("the last of the day")
    /// rather than a visual claim the image duplicates, and mood makes no claim about what the image owns at all.
    /// </para>
    /// </summary>
    [Fact]
    public void Derive_LocationBinding_RemovesEveryRoomCarryingElement()
    {
        var fields = ReferenceBindingPromptRemoval.Derive([Binding("Location")]);

        // The room is repeated across the whole brief, so every place that restates it is removed. Leaving the
        // neighbours behind is how a bound location still produced a room-dominated prompt (2026-10-20).
        Assert.Equal(
            [
                "frozenState.location", "frozenState.environment", "frozenState.lighting",
                "frozenState.objects", "frozenState.visualDescription", "frozenState.continuityState",
                "moment.frozenState", "moment.compositionRationale",
                "continuity.start.location", "continuity.start.objectStates", "continuity.start.lighting", "continuity.start.characterStates", "continuity.start.stateSummary",
                "continuity.end.location", "continuity.end.objectStates", "continuity.end.lighting", "continuity.end.characterStates", "continuity.end.stateSummary"
            ],
            fields.Select(field => field.ElementKey));
        Assert.All(fields, field => Assert.True(field.Removed));
        Assert.DoesNotContain(fields, field => field.ElementKey == "frozenState.timeOfDay");
        Assert.DoesNotContain(fields, field => field.ElementKey == "frozenState.mood");
    }

    [Fact]
    public void Derive_TextOnlyBinding_ContributesNothing()
    {
        // It describes no image, so there is nothing for it to replace.
        Assert.Empty(ReferenceBindingPromptRemoval.Derive([Binding("Location", strategy: "TextOnly")]));
    }

    [Fact]
    public void Derive_BindingWithoutAReferenceImage_ContributesNothing()
    {
        Assert.Empty(ReferenceBindingPromptRemoval.Derive([Binding("Location", withReference: false)]));
    }

    [Fact]
    public void Derive_FaceBindingWithAnActorKey_RemovesThatCharactersFaceAppearance()
    {
        var fields = ReferenceBindingPromptRemoval.Derive([Binding("Identity", actorKey: "p-becky")]);

        var field = Assert.Single(fields);
        Assert.Equal("character:p-becky.appearance.face", field.ElementKey);
    }

    /// <summary>
    /// A per-character element can only be addressed when the binding names the character. This is a KNOWN GAP, not
    /// a design choice: the legacy reference panels do not populate an actor key yet, and B130-011 fills them from
    /// the Moment cast. Deriving nothing is the honest behaviour until then - guessing which character the operator
    /// meant would remove the WRONG prose, which is worse than removing none.
    /// </summary>
    [Fact]
    public void Derive_PerCharacterBindingWithoutAnActorKey_ContributesNothing()
    {
        Assert.Empty(ReferenceBindingPromptRemoval.Derive([Binding("Identity")]));
    }

    [Fact]
    public void Derive_ExplicitKindIsPreferredOverTheLegacyElementKey()
    {
        var fields = ReferenceBindingPromptRemoval.Derive(
            [Binding("Location", kind: nameof(ImageStepSlotKind.Wardrobe), actorKey: "p-dean")]);

        var field = Assert.Single(fields);
        Assert.Equal("character:p-dean.clothing", field.ElementKey);
    }

    [Fact]
    public void Derive_UnknownElementKey_ContributesNothing()
    {
        Assert.Empty(ReferenceBindingPromptRemoval.Derive([Binding("SomethingElse")]));
    }

    /// <summary>
    /// A body view is composed at the same angle as the face views, so the body image carries that angle's FACE as
    /// well as the build. B-166 D10 therefore removes BOTH elements for a body binding, and a face binding for the
    /// same character adds nothing that is not already removed — the list is de-duplicated, not duplicated.
    /// </summary>
    [Fact]
    public void Derive_DuplicateScopesAcrossBindings_AreEmittedOnce()
    {
        var fields = ReferenceBindingPromptRemoval.Derive(
        [
            Binding("Identity", kind: nameof(ImageStepSlotKind.Face), actorKey: "p-becky"),
            Binding("Body", kind: nameof(ImageStepSlotKind.Body), actorKey: "p-becky")
        ]);

        Assert.Equal(
            ["character:p-becky.appearance.face", "character:p-becky.appearance.build"],
            fields.Select(field => field.ElementKey));
    }

    [Fact]
    public void DeriveForCharacterLoras_RemovesTheFaceElementForEachCharacter()
    {
        // B-166 D6: a character LoRA carries identity the way a face reference does.
        var fields = ReferenceBindingPromptRemoval.DeriveForCharacterLoras(
        [
            new ResolvedCharacterLora("a1", "p-becky", "Becky", "becky_token", "becky.safetensors", 0.8, "sha")
        ]);

        var field = Assert.Single(fields);
        Assert.Equal("character:p-becky.appearance.face", field.ElementKey);
        Assert.True(field.Removed);
    }

    [Fact]
    public void DeriveForCharacterLoras_WithNoSelection_ContributesNothing()
    {
        Assert.Empty(ReferenceBindingPromptRemoval.DeriveForCharacterLoras(null));
        Assert.Empty(ReferenceBindingPromptRemoval.DeriveForCharacterLoras([]));
    }

    [Fact]
    public void DeriveForCharacterLoras_WithoutACharacter_FailsFast()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ReferenceBindingPromptRemoval.DeriveForCharacterLoras(
            [
                new ResolvedCharacterLora("a1", "  ", "Becky", "becky_token", "becky.safetensors", 0.8, "sha")
            ]));

        Assert.Contains("a1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImpliesRemoval_AnswersForExactlyTheBindingsDeriveActsOn()
    {
        // The caller that refuses a reference the model cannot carry asks THIS, so it cannot refuse a binding this
        // class would never have removed anything for.
        Assert.True(ReferenceBindingPromptRemoval.ImpliesRemoval(Binding("Location")));
        Assert.False(ReferenceBindingPromptRemoval.ImpliesRemoval(Binding("Location", strategy: "TextOnly")));
        Assert.False(ReferenceBindingPromptRemoval.ImpliesRemoval(Binding("Location", withReference: false)));
        Assert.False(ReferenceBindingPromptRemoval.ImpliesRemoval(Binding("Identity")));
    }

    [Fact]
    public void Merge_NothingDerivable_ReturnsTheOperatorsOverridesUnchanged()
    {
        var userOverrides = new ScenePromptOverrides
        {
            Fields = [new ScenePromptFieldOverride { ElementKey = "moment.visibleAction", Value = "written by hand" }]
        };

        var merged = ReferenceBindingPromptRemoval.Merge(userOverrides, [Binding("Location", strategy: "TextOnly")]);

        Assert.Same(userOverrides, merged);
    }

    /// <summary>
    /// A binding supplies a DEFAULT. A deliberate operator edit outranks it, always - otherwise binding a location
    /// would silently discard prose the operator had written on purpose.
    /// </summary>
    [Fact]
    public void Merge_OperatorOverrideForTheSameElement_Wins()
    {
        var userOverrides = new ScenePromptOverrides
        {
            Fields = [new ScenePromptFieldOverride { ElementKey = "frozenState.location", Value = "a cathedral nave" }]
        };

        var merged = ReferenceBindingPromptRemoval.Merge(userOverrides, [Binding("Location")])!;

        var kept = Assert.Single(merged.Fields, field => field.ElementKey == "frozenState.location");
        Assert.Equal("a cathedral nave", kept.Value);
        Assert.False(kept.Removed);
        // ...while the element the operator said nothing about is still removed by the binding.
        Assert.Contains(merged.Fields, field => field.ElementKey == "frozenState.environment" && field.Removed);
        Assert.Contains(merged.Fields, field => field.ElementKey == "frozenState.lighting" && field.Removed);
    }

    [Fact]
    public void Merge_PreservesTheOperatorsRemovedCharacters()
    {
        var userOverrides = new ScenePromptOverrides { RemovedCharacters = ["p-dean"] };

        var merged = ReferenceBindingPromptRemoval.Merge(userOverrides, [Binding("Location")])!;

        Assert.Equal(["p-dean"], merged.RemovedCharacters);
    }

    [Fact]
    public void Merge_NoBindings_ReturnsTheOperatorsOverridesUnchanged()
    {
        var userOverrides = new ScenePromptOverrides();

        Assert.Same(userOverrides, ReferenceBindingPromptRemoval.Merge(userOverrides, null));
        Assert.Same(userOverrides, ReferenceBindingPromptRemoval.Merge(userOverrides, []));
    }
}
