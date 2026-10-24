using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The reference images a step binds have to be NAMED to the image model, by index, in the order the render sends
/// them.
///
/// <para>
/// Qwen's own prompt-enhancer system prompt requires it — "For Multi-Image Input (N &gt;= 2), the rewritten
/// instruction MUST use &lt;image1&gt;, &lt;image2&gt;, ... This tagging format is mandatory and non-negotiable. For
/// single-image input (N = 1), do NOT use tags." — and ComfyUI's 2.1 encoder passes the prompt through unchanged, so
/// nothing else supplies the indices. Measured 2026-10-03 on the app's own references at one pinned seed: the same two
/// images scored outer-ring histogram L1 1.574 against the bound shed with no role text and 0.887 with it, better than
/// the shed-alone control (0.936).
/// </para>
/// </summary>
public sealed class ReferenceRoleClauseTests
{
    /// <summary>
    /// A BODY reference's pack view label says which way the person faces, and that must reach the prompt — the
    /// "straight-on view" flip (2026-10-22) happened because the body clause named build/skin but never orientation.
    /// </summary>
    [Fact]
    public void BlockFor_BackBodyView_StatesThePersonIsViewedFromBehind()
    {
        var block = ReferenceRoleClauses.BlockFor(
        [
            new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Body, "Back · Unclothed", "p-becky"),
            new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Location, "Bench Side")
        ]);

        Assert.Contains("viewed from BEHIND — their back faces the camera", block, StringComparison.Ordinal);
    }

    [Fact]
    public void BlockFor_FrontBodyView_StatesThePersonIsViewedFromTheFront()
    {
        var block = ReferenceRoleClauses.BlockFor(
        [
            new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Body, "Front · Clothed", "p-becky"),
            new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Location, "Bench Side")
        ]);

        Assert.Contains("viewed from the FRONT", block, StringComparison.Ordinal);
        Assert.DoesNotContain("viewed from BEHIND", block, StringComparison.Ordinal);
    }

    [Fact]
    public void BlockFor_UnlabelledBodyView_StatesNoOrientation()
    {
        var block = ReferenceRoleClauses.BlockFor(
        [
            new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Body, null, "p-becky"),
            new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Location, "Bench Side")
        ]);

        Assert.DoesNotContain("viewed from", block, StringComparison.Ordinal);
    }

    private const string FaceImage = "282f5b91ba7d4dba85140ae006aa06ee";
    private const string BuildImage = "37f0fa3f2dc3478eb47fb253beb1a104";

    /// <summary>
    /// THE ONE THAT MATTERS. The planned order and the sent order are not the same list: the render resolves
    /// pack-supplied images first (a pack image lives in its own store) and the approved-asset references after, so a
    /// step that binds a LOCATION at ordinal 1 and a pack FACE at ordinal 2 still sends the face FIRST. Numbering a
    /// clause from the planned order would name the room <c>&lt;image1&gt;</c> while the model received the face in
    /// slot 1 — every tag after the first would point at the wrong image, and nothing would fail.
    /// </summary>
    /// <summary>
    /// A block that carries PEOPLE states the invariant categories and supplies no values for them.
    ///
    /// <para>
    /// This is Qwen's own worked multi-reference example ("keep the two people's facial features, hairstyle and
    /// clothing appearance completely unchanged") and its prompt-enhancer says why the categories and not the values:
    /// "the more concretely you describe something you meant to keep, the more likely it drifts". The appearance prose
    /// a reference supplies is removed from the brief for the same reason, so a value here would contradict that
    /// removal and reintroduce the duplicate description it exists to prevent (B-157, 2026-10-12).
    /// </para>
    /// </summary>
    [Fact]
    public void BlockFor_WithPeopleReferences_StatesOneBlanketPreservationClauseAndNoValues()
    {
        var block = ReferenceRoleClauses.BlockFor(
        [
            new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Face, "Front", "p-becky"),
            new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Body, "Front · Clothed", "p-becky")
        ]);

        Assert.Contains(
            "Keep each person's face, hair, skin tone, build and clothing exactly",
            block,
            StringComparison.Ordinal);

        // Categories, never values: nothing enumerates an attribute the reference already shows.
        Assert.DoesNotContain("blue eyes", block, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("olive", block, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hair colour", block, StringComparison.OrdinalIgnoreCase);

        // The clause DEFERS to the instruction — but only where there IS one. An EDIT carries the operator's instruction
        // (its source image occupies slot 1); a render's caption IS the prompt, so the deferral sentence would tell the
        // image model to obey an instruction that does not exist (operator report 2026-10-23).
        Assert.Contains(
            "Where the instruction explicitly changes that person's view, pose, stance or clothing, follow the instruction",
            ReferenceRoleClauses.BlockFor(
                [
                    new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Face, "Front", "p-becky"),
                    new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Body, "Front · Clothed", "p-becky")
                ],
                ReferenceRoleClauses.LeadingImageCount.Edit),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Where the instruction explicitly changes that person's view, pose, stance or clothing",
            block,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The clause is only emitted by the untagged path when the role carries directions (<c>CarriesStatement</c>), so the
    /// precedence sentence is pinned where it is actually read: the numbered multi-reference block above.
    /// </summary>
    [Fact]
    public void BlockFor_WithAWardrobeReference_KeepsTheClothingOwnerAndDefersToTheInstruction()
    {
        var block = ReferenceRoleClauses.BlockFor(
        [
            new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Body, "Front · Clothed", "p-becky"),
            new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Wardrobe, "Jean Shorts", "p-becky")
        ]);

        // The WARDROBE reference owns the clothing...
        Assert.Contains("keep the clothing exactly as the WARDROBE reference shows it", block, StringComparison.Ordinal);

        // ...and the instruction still wins where it explicitly changes something (an EDIT carries one).
        Assert.Contains(
            "Where the instruction explicitly changes that person's view, pose, stance or clothing, follow the instruction",
            ReferenceRoleClauses.BlockFor(
                [
                    new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Body, "Front · Clothed", "p-becky"),
                    new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Wardrobe, "Jean Shorts", "p-becky")
                ],
                ReferenceRoleClauses.LeadingImageCount.Edit),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A LOCATION reference must place the frame EARLY, not only in the block.
    ///
    /// <para>
    /// The block has always said "this image IS the room", and that was not enough. Measured on SceneImage 1cc55900
    /// (2026-10-18): an action-only intent compiled to pure body action with no setting, the location was sent as
    /// &lt;image3&gt; and named in the block — and the render still came back on the identity plate's plain grey
    /// STUDIO backdrop, because a full-figure person reference is reproduced wholesale and nothing early in the prompt
    /// placed her anywhere. The anchor is therefore written deterministically, ahead of the prose.
    /// </para>
    /// </summary>
    [Fact]
    public void AppendToImagePrompt_WithALocationReference_PutsTheSettingAnchorFirst()
    {
        var composed = ReferenceRoleClauses.AppendToImagePrompt(
            "She lifts the hem of her top.",
            [
                new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Body, "Front · Unclothed", "p0"),
                new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Location, "Clothesline")
            ]);

        var anchor = "The setting of this picture is the place shown in <image2>: that place supplies the background "
            + "of the frame, and no person reference supplies the background.";

        Assert.StartsWith(anchor, composed, StringComparison.Ordinal);
        Assert.Contains("She lifts the hem of her top.", composed, StringComparison.Ordinal);
        // …and the prose still precedes the block, so the anchor is the very first thing the model reads.
        Assert.True(
            composed.IndexOf(anchor, StringComparison.Ordinal) < composed.IndexOf("REFERENCE IMAGES", StringComparison.Ordinal));
    }

    [Fact]
    public void AppendToImagePrompt_WithoutALocationReference_WritesNoAnchor()
    {
        var composed = ReferenceRoleClauses.AppendToImagePrompt(
            "She lifts the hem of her top.",
            [
                new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Body, "Front · Unclothed", "p0"),
                new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Wardrobe, "Jean Shorts", "p0")
            ]);

        Assert.DoesNotContain("The setting of this picture", composed, StringComparison.Ordinal);
        Assert.StartsWith("She lifts the hem of her top.", composed, StringComparison.Ordinal);
    }

    /// <summary>
    /// An EDIT starts from a source image that already carries its own setting, so the anchor would contradict the
    /// frame being edited. The same rule therefore applies to generation only.
    /// </summary>
    [Fact]
    public void AppendToEditInstruction_WithALocationReference_WritesNoAnchor()
    {
        var composed = ReferenceRoleClauses.AppendToEditInstruction(
            "Remove the chair from the porch.",
            [
                (ImageStepSlotKind.Location, "Clothesline", (string?)null, null)
            ]);

        Assert.DoesNotContain("The setting of this picture", composed, StringComparison.Ordinal);
        Assert.StartsWith("Remove the chair from the porch.", composed, StringComparison.Ordinal);
    }

    /// <summary>
    /// A WARDROBE reference owns the clothing. Before this, the blanket clause bundled "clothing" into the sentence
    /// about the reference that carries the PERSON — and a body reference is a clothed photograph, so it won. Measured
    /// 2026-10-17 on SceneImages 9b36a8c7: a 'Jean Shorts' wardrobe binding rendered the body plate's T-shirt and jeans.
    /// </summary>
    [Fact]
    public void BlockFor_WithAWardrobeReference_AttributesClothingToTheWardrobeNotThePersonReference()
    {
        var block = ReferenceRoleClauses.BlockFor(
        [
            new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Body, "Profile Right · Clothed", "p0"),
            new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Wardrobe, "Jean Shorts", "p0"),
            new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Location, "Clothesline Close 2")
        ]);

        Assert.Contains("keep the clothing exactly as the WARDROBE reference shows it", block, StringComparison.Ordinal);
        Assert.Contains("is not the outfit in this frame", block, StringComparison.Ordinal);
        Assert.DoesNotContain("skin tone, build and clothing exactly", block, StringComparison.Ordinal);
    }

    /// <summary>
    /// A block of scene-level references only — a room and a skeleton — says nothing about people. The clause claims
    /// exactly what the references carry, so it cannot assert an appearance no image supplies.
    /// </summary>
    [Fact]
    public void BlockFor_WithOnlySceneReferences_ClaimsNothingAboutPeople()
    {
        var block = ReferenceRoleClauses.BlockFor(
        [
            new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Location, "Yard"),
            new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Pose, "Stance")
        ]);

        Assert.DoesNotContain("Keep each person's", block, StringComparison.Ordinal);
        // The room's role clause says the image IS the room. The old extra line ("Frame the shot inside that room …
        // name the setting only as its kind of place … do not describe its structure") was an instruction to the
        // CAPTION WRITER and had no business in the image prompt (operator, 2026-10-22).
        Assert.Contains("this image IS the room", block, StringComparison.Ordinal);
        Assert.DoesNotContain("Frame the shot inside that room", block, StringComparison.Ordinal);
    }

    [Fact]
    public void InSendOrder_PutsPackSuppliedImagesFirst_WhenThePlannedOrderDoesNot()
    {
        var location = ApprovedAsset("Location", ordinal: 1, label: "Workbench Back");
        var face = PackFace(ordinal: 2, label: "Front");

        var ordered = ReferenceBindingShape.InSendOrder([location, face]);

        Assert.Equal([face, location], ordered);
    }

    /// <summary>
    /// ...and the clause is numbered from that same order, end to end: the pack face is <c>&lt;image1&gt;</c> and the
    /// room is <c>&lt;image2&gt;</c>, even though the room was bound first.
    /// </summary>
    [Fact]
    public void ForPreprocessor_NumbersTheImagesInSendOrder_NotInPlannedOrder()
    {
        var location = ApprovedAsset("Location", ordinal: 1, label: "Workbench Back");
        var face = PackFace(ordinal: 2, label: "Front");

        var clause = ReferenceRoleClauses.ForPreprocessor([location, face]);

        var faceTag = clause.IndexOf("<image1>", StringComparison.Ordinal);
        var locationTag = clause.IndexOf("<image2>", StringComparison.Ordinal);
        Assert.True(faceTag >= 0, "the first sent image must be tagged <image1>");
        Assert.True(locationTag >= 0, "the second sent image must be tagged <image2>");
        Assert.Contains("Front", clause[faceTag..locationTag], StringComparison.Ordinal);
        Assert.Contains("Workbench Back", clause[locationTag..], StringComparison.Ordinal);
    }

    /// <summary>
    /// A single reference gets NO tags. That is Qwen's own rule for N = 1 ("do NOT use tags — refer to the image
    /// naturally"), and it is also what every single-reference render already did successfully, so introducing a tag
    /// there would change behaviour nobody reported a problem with.
    /// </summary>
    [Fact]
    public void OneReference_ProducesNoClauseAndLeavesTheImagePromptAlone()
    {
        var bindings = new[] { ApprovedAsset("Location", ordinal: 1, label: "Workbench Back") };

        Assert.Equal(string.Empty, ReferenceRoleClauses.ForPreprocessor(bindings));
        Assert.Equal(
            "a dim shed",
            ReferenceRoleClauses.AppendToImagePrompt("a dim shed", ReferenceRoleClauses.RolesFor(bindings)));
    }

    /// <summary>
    /// A single reference that HAS something stated about it IS described — and still carries no tag.
    ///
    /// <para>
    /// The vendor's N = 1 tagging rule used to be carried by collapsing the role list, so "may not be numbered" also
    /// meant "may not be described": a location-only composition sent its backdrop with no word about what is in it,
    /// and B-160's reading of that image could not reach the prompt until a face or body was bound beside it (reported
    /// 2026-10-13). Numbering and describing are two decisions; only the first is the vendor's.
    /// </para>
    /// </summary>
    [Fact]
    public void OneReferenceWithAStatementIsDescribedWithoutATag()
    {
        var binding = ApprovedAsset("Location", ordinal: 1, label: "Workbench Back");
        binding.Directions = new ReferenceDirections(
            null, null, "the trailer with the covered deck", ReferenceDirectionSource.Analysis);
        var bindings = new[] { binding };
        var roles = ReferenceRoleClauses.RolesFor(bindings);

        var clause = ReferenceRoleClauses.ForPreprocessor(bindings);
        var block = ReferenceRoleClauses.BlockFor(roles);
        var composed = ReferenceRoleClauses.AppendToImagePrompt("a dim shed", roles);

        // The PRE-PROCESSOR is told what the image supplies and what it must NOT write. It is deliberately NOT handed
        // the analysis text or the image-model sentence: both made it reproduce the room verbatim (measured live
        // 2026-10-20, production group 449e9148).
        Assert.Contains("is the LOCATION reference", clause, StringComparison.Ordinal);
        Assert.Contains("Name the place only by its KIND", clause, StringComparison.Ordinal);
        Assert.DoesNotContain("What this image contains", clause, StringComparison.Ordinal);
        Assert.DoesNotContain("the trailer with the covered deck", clause, StringComparison.Ordinal);
        Assert.DoesNotContain("Reproduce its structure", clause, StringComparison.Ordinal);

        // The IMAGE MODEL still gets the analysis and the "this image IS the room" sentence: it is conditioning on the
        // picture, not writing prose about it. The analysis reading itself is not restated (2026-10-21).
        Assert.DoesNotContain("the trailer with the covered deck", block, StringComparison.Ordinal);
        Assert.Contains("this image IS the room", block, StringComparison.Ordinal);
        // Writer-facing instructions stay out of the picture prompt (operator, 2026-10-22).
        Assert.DoesNotContain("Frame the shot inside that room", block, StringComparison.Ordinal);
        Assert.DoesNotContain("Name the setting only as its kind of place", block, StringComparison.Ordinal);

        Assert.StartsWith("a dim shed", composed, StringComparison.Ordinal);

        // The tag rule is untouched: one image is never numbered, and neither clause may imply a number.
        Assert.Empty(ReferenceRoleClauses.TagsFor(bindings));
        Assert.DoesNotContain("<image", clause, StringComparison.Ordinal);
        Assert.DoesNotContain("<image", block, StringComparison.Ordinal);
        Assert.DoesNotContain("<image", composed, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pre-processor's location line must not license the room, and the closing rule must not tie the prohibition
    /// to "something the brief already supplies" — for a bound location the brief does NOT supply it, so that wording
    /// left the room free to be described (that is exactly what the 2026-10-20 Becky POV prompt did).
    /// </summary>
    [Fact]
    public void ForPreprocessor_LocationNeverRestatesTheRoom_WhateverTheBriefSupplies()
    {
        var binding = ApprovedAsset("Location", ordinal: 1, label: "Indoor Back");
        binding.Directions = new ReferenceDirections(
            null, null, "corrugated metal walls", ReferenceDirectionSource.Analysis);

        var clause = ReferenceRoleClauses.ForPreprocessor([binding]);

        Assert.Contains("it supplies the setting", clause, StringComparison.Ordinal);
        Assert.Contains("Do NOT restate any of it in the caption", clause, StringComparison.Ordinal);
        Assert.Contains("whether or not the brief also states it", clause, StringComparison.Ordinal);
        Assert.DoesNotContain("something the brief already supplies", clause, StringComparison.Ordinal);
        Assert.DoesNotContain("its structure, surfaces, objects and its own lighting", clause, StringComparison.Ordinal);
    }

    /// <summary>
    /// A placement direction (side / facing) still reaches the pre-processor; the free-text NOTE does not, because it
    /// is a description of what the picture contains and the caption writer copies it.
    /// </summary>
    [Fact]
    public void ForPreprocessor_KeepsPlacementDirections_AndDropsTheContentNote()
    {
        var binding = ApprovedAsset("Location", ordinal: 1, label: "Indoor Back");
        binding.Directions = new ReferenceDirections(
            FrameHorizontal.Right, ReferenceFacing.AwayFromViewer,
            "corrugated metal walls and a wooden floor showing cracks", ReferenceDirectionSource.Analysis);

        var clause = ReferenceRoleClauses.ForPreprocessor([binding]);

        Assert.Contains("on the RIGHT of the frame", clause, StringComparison.Ordinal);
        Assert.Contains("facing away from the camera", clause, StringComparison.Ordinal);
        Assert.DoesNotContain("corrugated metal walls", clause, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of the same decision: a single reference with NOTHING stated about it is left exactly as every
    /// single-reference render has always been. The automatic role sentence is not invented for it — that would change
    /// renders nobody reported a problem with.
    /// </summary>
    [Fact]
    public void OneReferenceWithNothingStatedIsLeftExactlyAsItWas()
    {
        ReferenceRoleClauses.ReferenceRole[] roles = [new(ImageStepSlotKind.Face, "Front")];

        Assert.Equal(string.Empty, ReferenceRoleClauses.BlockFor(roles));
        Assert.Equal(string.Empty, ReferenceRoleClauses.ForPreprocessor([ApprovedAsset("Face", ordinal: 1, label: "Front")]));
        Assert.Equal("a dim shed", ReferenceRoleClauses.AppendToImagePrompt("a dim shed", roles));
    }

    /// <summary>
    /// ONE reference on an EDIT is NUMBERED <c>&lt;image2&gt;</c> — because the model receives TWO images: the source in
    /// slot 1 and the reference beside it. The vendor rule the tag owner quotes bites at N &gt;= 2 IMAGES, and it was
    /// being applied to the reference COUNT instead, so a one-reference edit emitted no tag at all and a bound face
    /// reference reached the model unnamed (reported live 2026-10-15: the pack face was composited into the frame as a
    /// second face).
    /// </summary>
    [Fact]
    public void AnEditWithOneReferenceNumbersItFromTheSecondImage()
    {
        var instruction = ReferenceRoleClauses.AppendToEditInstruction(
            "replace the man with the woman",
            [(ImageStepSlotKind.Face, "3/4 Right", null, null)]);

        Assert.StartsWith("replace the man with the woman", instruction, StringComparison.Ordinal);
        Assert.Contains("<image2>", instruction, StringComparison.Ordinal);
        Assert.DoesNotContain("<image1>", instruction, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same one-reference EDIT describes its reference too — the tag and the description are separate decisions
    /// and both now apply, so an operator's own statement about the image still reaches the editor.
    /// </summary>
    [Fact]
    public void AnEditWithOneStatementCarryingReferenceKeepsTheStatementBesideTheTag()
    {
        var instruction = ReferenceRoleClauses.AppendToEditInstruction(
            "replace the man with the woman",
            [
                (ImageStepSlotKind.Location, "Workbench Back", "the-shed",
                    new ReferenceDirections(null, ReferenceFacing.Right, "the trailer facing its neighbour"))
            ]);

        Assert.StartsWith("replace the man with the woman", instruction, StringComparison.Ordinal);
        Assert.Contains("<image2>", instruction, StringComparison.Ordinal);
        Assert.Contains("the trailer facing its neighbour", instruction, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE EDIT OFFSET. On an edit the SOURCE image occupies the model's slot 1 (the 2.1 edit graph wires it to
    /// <c>images.image_1</c> and reference *i* to <c>image_{i+2}</c>), so the first reference is
    /// <c>&lt;image2&gt;</c>. Numbering it <c>&lt;image1&gt;</c> would name the source image as if it were the
    /// reference — every tag pointing one image early, with nothing failing.
    /// </summary>
    [Fact]
    public void AnEditInstructionNumbersFromTheSecondImageBecauseTheSourceIsFirst()
    {
        var instruction = ReferenceRoleClauses.AppendToEditInstruction(
            "replace the man with the woman",
            [
                (ImageStepSlotKind.Face, "Front", null, null),
                (ImageStepSlotKind.Location, "Workbench Back", null, null)
            ]);

        var faceTag = instruction.IndexOf("<image2>", StringComparison.Ordinal);
        var locationTag = instruction.IndexOf("<image3>", StringComparison.Ordinal);
        Assert.True(faceTag >= 0, "the first edit reference must be <image2>: the source image is slot 1");
        Assert.True(locationTag > faceTag, "the second edit reference must be <image3>");
        Assert.DoesNotContain("<image1>", instruction, StringComparison.Ordinal);
        Assert.StartsWith("replace the man with the woman", instruction, StringComparison.Ordinal);
    }

    /// <summary>
    /// The tag rule counts the IMAGES THE MODEL RECEIVES — the references plus whatever the surface sends ahead of
    /// them — and that is one rule with one owner. A RENDER of one reference sends one image and gets no tag; an EDIT of
    /// one reference sends two and gets <c>&lt;image2&gt;</c>; neither sends a tag at all when there is nothing to
    /// number.
    /// </summary>
    [Fact]
    public void TheTagRuleCountsTheImagesTheModelReceives()
    {
        Assert.False(ReferenceRoleClauses.SendsTags(0, ReferenceRoleClauses.LeadingImageCount.Edit));
        Assert.False(ReferenceRoleClauses.SendsTags(1, ReferenceRoleClauses.LeadingImageCount.Generate));
        Assert.True(ReferenceRoleClauses.SendsTags(1, ReferenceRoleClauses.LeadingImageCount.Edit));
        Assert.True(ReferenceRoleClauses.SendsTags(2, ReferenceRoleClauses.LeadingImageCount.Generate));

        // …and the description-only route is unchanged: a single image is never counted as tagged.
        Assert.Equal(0, ReferenceRoleClauses.TaggedReferenceCount(1, ReferenceRoleClauses.LeadingImageCount.Generate));
        Assert.Equal(1, ReferenceRoleClauses.TaggedReferenceCount(1, ReferenceRoleClauses.LeadingImageCount.Edit));
    }

    /// <summary>
    /// A render of ONE reference is still neither numbered nor described unless something is stated about it — the
    /// generate-path guarantee the 2026-10-13 decision put in place is untouched by the edit rule beside it.
    /// </summary>
    [Fact]
    public void ARenderWithOneReferenceAndNoStatementIsStillLeftAlone()
    {
        var instruction = ReferenceRoleClauses.AppendToImagePrompt(
            "a woman on a deck",
            [new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Location, "Back")]);

        Assert.Equal("a woman on a deck", instruction);
    }

    /// <summary>
    /// A render numbers from image 1 and an edit from image 2, and the difference is ONE parameter rather than a
    /// second copy of the numbering rule. Pinned together so a change to either cannot quietly move the other.
    /// </summary>
    [Fact]
    public void TheGenerateAndEditNumberingsDifferByExactlyTheLeadingImage()
    {
        ReferenceRoleClauses.ReferenceRole[] roles =
        [
            new(ImageStepSlotKind.Face, "Front"),
            new(ImageStepSlotKind.Location, "Back")
        ];

        var generated = ReferenceRoleClauses.BlockFor(roles, ReferenceRoleClauses.LeadingImageCount.Generate);
        var edited = ReferenceRoleClauses.BlockFor(roles, ReferenceRoleClauses.LeadingImageCount.Edit);

        Assert.Contains("<image1>", generated, StringComparison.Ordinal);
        Assert.Contains("<image2>", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("<image3>", generated, StringComparison.Ordinal);
        Assert.Contains("<image2>", edited, StringComparison.Ordinal);
        Assert.Contains("<image3>", edited, StringComparison.Ordinal);
        Assert.DoesNotContain("<image1>", edited, StringComparison.Ordinal);
    }

    /// <summary>A leading count that would number a reference before &lt;image1&gt; is refused rather than trusted.</summary>
    [Fact]
    public void ANegativeLeadingImageCountIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReferenceRoleClauses.BlockFor(
            [
                new(ImageStepSlotKind.Face, "Front"),
                new(ImageStepSlotKind.Location, "Back")
            ],
            leadingImages: -1));
    }

    /// <summary>
    /// The media-edit handler must APPLY the role clause to the instruction it sends, not merely be able to. The
    /// numbering is only worth having if the prompt the model reads carries it.
    /// </summary>
    [Fact]
    public void TheMediaEditHandlerAppendsTheRoleClauseToTheInstructionItSends()
    {
        var handler = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "DreamGenClone.Web", "Application", "RolePlay", "Editing",
            "MediaEditImageEditingJobHandler.cs"));

        Assert.Contains("FinalImagePromptPlanBuilder.BuildEditInstruction(", handler, StringComparison.Ordinal);
        Assert.Contains(
            "parts.References.Select(reference => (reference.SlotKind, reference.Description, reference.AssetContainerId, reference.Directions))",
            handler,
            StringComparison.Ordinal);
        Assert.Contains("ExecuteAsync(plan, instruction, parts.References, resolved, source, cancellationToken)", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void NoReferences_ProduceNothing()
    {
        Assert.Equal(string.Empty, ReferenceRoleClauses.ForPreprocessor(null));
        Assert.Equal(string.Empty, ReferenceRoleClauses.ForPreprocessor([]));
        Assert.Equal("prompt", ReferenceRoleClauses.AppendToImagePrompt("prompt", null));
        Assert.Equal(string.Empty, ReferenceRoleClauses.BlockFor(null));
    }

    /// <summary>
    /// A text-only binding describes no image, so it is never numbered. Numbering it would shift every tag after it
    /// onto an image that is not there.
    /// </summary>
    [Fact]
    public void ATextOnlyBindingIsNotNumbered()
    {
        var bindings = new[]
        {
            ApprovedAsset("Location", ordinal: 1, label: "Workbench Back"),
            TextOnly("Wardrobe", ordinal: 2),
            PackFace(ordinal: 3, label: "Front")
        };

        var clause = ReferenceRoleClauses.ForPreprocessor(bindings);

        Assert.Contains("<image1>", clause, StringComparison.Ordinal);
        Assert.Contains("<image2>", clause, StringComparison.Ordinal);
        Assert.DoesNotContain("<image3>", clause, StringComparison.Ordinal);
    }

    /// <summary>
    /// LOCATION references from DIFFERENT containers are separate places in one frame, not several views of one room.
    ///
    /// <para>
    /// Reported 2026-10-12: the operator bound two trailer fronts in order to show the trailers side by side facing
    /// each other. Each reference was told "this image IS the room … do not replace it with the setting of any other
    /// reference image", three mutually exclusive instructions for the same frame, and the closing line named one
    /// singular "that room". The clause cannot know how the places sit — that is the instruction's job — so it states
    /// only what the images themselves carry: different places, one frame, all of them visible.
    /// </para>
    /// </summary>
    [Fact]
    public void LocationsFromDifferentContainersAreSeparatePlacesInOneFrame()
    {
        var clause = ReferenceRoleClauses.BlockFor(
            ReferenceRoleClauses.RolesFor(
            [
                ApprovedAsset("Location", ordinal: 1, label: "Couples Front", assetId: "trailer-couples"),
                ApprovedAsset("Location", ordinal: 2, label: "Other Front", assetId: "trailer-other")
            ]));

        // Neither may claim exclusivity: that is the contradiction this branch removes.
        Assert.DoesNotContain("this image IS the room", clause, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "do not replace it with the setting of any other reference image",
            clause,
            StringComparison.Ordinal);

        // Both are named as places sharing the frame.
        Assert.Equal(2, clause.Split("is a SEPARATE PLACE in this frame").Length - 1);
        Assert.Contains("Do not merge it into another place", clause, StringComparison.Ordinal);

        // The operator names each trailer, so the prose can refer to them by name.
        Assert.Contains("Couples Front", clause, StringComparison.Ordinal);
        Assert.Contains("Other Front", clause, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two views of ONE place keep the exclusivity they have always had. This is the case the Location slot's
    /// multi-value allowance exists for — "keeps the same building the same building from every angle" — and it is the
    /// case the operator's own workflow uses ("I will use the first approved image as reference to create the other
    /// sides"). Container identity is the only thing that separates it from the two-trailer case, so it is pinned here
    /// as well as in the handler test that reported it.
    /// </summary>
    [Fact]
    public void LocationsFromOneContainerRemainTheSameRoom()
    {
        var clause = ReferenceRoleClauses.BlockFor(
            ReferenceRoleClauses.RolesFor(
            [
                ApprovedAsset("Location", ordinal: 1, label: "Outdoor Front", assetId: "the-shed"),
                ApprovedAsset("Location", ordinal: 2, label: "Workbench Back", assetId: "the-shed")
            ]));

        Assert.Equal(2, clause.Split("this image IS the room").Length - 1);
        Assert.Contains(
            "do not replace it with the setting of any other reference image",
            clause,
            StringComparison.Ordinal);
        Assert.DoesNotContain("is a SEPARATE PLACE in this frame", clause, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unknown container is NOT evidence of separate places: absent that information the clause keeps the
    /// behaviour it has always had, because guessing structures would change what a currently-correct render means.
    ///
    /// <para>
    /// Asserted on roles directly: a binding that carries no asset container is not a renderable reference at all
    /// (<c>SuppliesImage</c> is false for it), so this fallback can only be exercised at this level.
    /// </para>
    /// </summary>
    [Fact]
    public void LocationsWithNoContainerAtAllRemainTheSameRoom()
    {
        var clause = ReferenceRoleClauses.BlockFor(
        [
            new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Location, "Outdoor Front"),
            new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Location, "Workbench Back")
        ]);

        Assert.Contains("this image IS the room", clause, StringComparison.Ordinal);
        Assert.DoesNotContain("Frame the shot inside that room", clause, StringComparison.Ordinal);
        Assert.DoesNotContain("is a SEPARATE PLACE in this frame", clause, StringComparison.Ordinal);
    }

    /// <summary>
    /// ...and the appended block states their COEXISTENCE rather than framing the shot inside one "room" — without
    /// inventing a relationship it has no source for.
    /// </summary>
    [Fact]
    public void SeparatePlacesAppendACoexistenceStatementInsteadOfASingularRoom()
    {
        var block = ReferenceRoleClauses.BlockFor(
        [
            new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Location, "Couples Front", ContainerKey: "trailer-couples"),
            new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Location, "Other Front", ContainerKey: "trailer-other")
        ]);

        Assert.DoesNotContain("Frame the shot inside that room", block, StringComparison.Ordinal);
        Assert.Contains("stands in the SAME frame", block, StringComparison.Ordinal);
        Assert.Contains("do not render only one of them", block, StringComparison.Ordinal);

        // It must not assert a layout: side-by-side, facing, left/right are the instruction's business, and inventing
        // one here would be a second, unowned decision about the frame.
        Assert.DoesNotContain("side by side", block, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("facing each other", block, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("on the left", block, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The one-place block states the room and nothing else: the writer-facing sentences ("frame the shot inside that
    /// room / name the setting only as its kind of place / do not describe its structure") are the caption writer's, and
    /// an image model does not name things.
    /// </summary>
    [Fact]
    public void OnePlaceBlockStatesTheRoomWithoutCaptionWriterInstructions()
    {
        var block = ReferenceRoleClauses.BlockFor(
        [
            new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Face, "Front", "p0"),
            new ReferenceRoleClauses.ReferenceRole(ImageStepSlotKind.Location, "Workbench Back", ContainerKey: "the-shed")
        ]);

        Assert.Contains("this image IS the room", block, StringComparison.Ordinal);
        Assert.Contains("the shot takes place inside it", block, StringComparison.Ordinal);
        Assert.DoesNotContain("Frame the shot inside that room", block, StringComparison.Ordinal);
        Assert.DoesNotContain("Name the setting only as its kind of place", block, StringComparison.Ordinal);
        Assert.DoesNotContain("stands in the SAME frame", block, StringComparison.Ordinal);
    }

    /// <summary>
    /// The room is stated as the room to the IMAGE MODEL, and explicitly protected from being replaced by another
    /// reference's setting — which is the failure that started this: a studio-lit identity plate carried its own
    /// background over the shed.
    /// </summary>
    [Fact]
    public void TheLocationLineSaysTheImageIsTheRoom()
    {
        var clause = ReferenceRoleClauses.BlockFor(
            ReferenceRoleClauses.RolesFor(
            [
                PackFace(ordinal: 1, label: "Front"),
                ApprovedAsset("Location", ordinal: 2, label: "Workbench Back")
            ]));

        Assert.Contains("this image IS the room", clause, StringComparison.Ordinal);
        Assert.Contains("Do not substitute a different room", clause, StringComparison.Ordinal);
    }

    /// <summary>
    /// A person plate must not donate its background or its lighting. Both identity plates in the reported case were
    /// studio shots, and the tagless render drifted +24 brighter than the shed it was supposed to be inside.
    /// </summary>
    [Fact]
    public void APersonPlateIsToldNotToDonateItsBackgroundOrLighting()
    {
        var clause = ReferenceRoleClauses.BlockFor(
            ReferenceRoleClauses.RolesFor(
            [
                PackFace(ordinal: 1, label: "Front"),
                ApprovedAsset("Location", ordinal: 2, label: "Workbench Back")
            ]));

        Assert.Contains("Do not carry over its plain studio background or its studio lighting", clause, StringComparison.Ordinal);

        // The PRE-PROCESSOR carries the same protection in the caption writer's terms: it must not describe the plate's
        // background as if it were the setting.
        var forPreprocessor = ReferenceRoleClauses.ForPreprocessor(
        [
            PackFace(ordinal: 1, label: "Front"),
            ApprovedAsset("Location", ordinal: 2, label: "Workbench Back")
        ]);
        Assert.Contains("stand in for the setting", forPreprocessor, StringComparison.Ordinal);
    }

    /// <summary>
    /// The finished image prompt carries the block too, generated from the bindings rather than left to the
    /// pre-processor: the tag format is not negotiable, and a language model that paraphrases it leaves the image
    /// model guessing again.
    /// </summary>
    [Fact]
    public void AppendToImagePrompt_AddsTheBlockWithoutDiscardingThePrompt()
    {
        var bindings = new[]
        {
            PackFace(ordinal: 1, label: "Front"),
            ApprovedAsset("Location", ordinal: 2, label: "Workbench Back")
        };

        var composed = ReferenceRoleClauses.AppendToImagePrompt(
            "A nude woman on a workbench, 35mm.",
            ReferenceRoleClauses.RolesFor(bindings));

        // The prompt is not discarded, but a LOCATION reference now places the frame AHEAD of it (2026-10-18): see
        // AppendToImagePrompt_WithALocationReference_PutsTheSettingAnchorFirst.
        Assert.Contains("A nude woman on a workbench, 35mm.", composed, StringComparison.Ordinal);
        Assert.StartsWith("The setting of this picture is the place shown in <image2>", composed, StringComparison.Ordinal);
        Assert.Contains("REFERENCE IMAGES — AUTHORITATIVE", composed, StringComparison.Ordinal);
        Assert.Contains("<image1> (Front)", composed, StringComparison.Ordinal);
        Assert.Contains("<image2> (Workbench Back)", composed, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unrecognised element key is not a reference this can name, and a binding whose kind cannot be resolved must
    /// not be numbered as though it were: the tags have to point at the images the render actually sends.
    /// </summary>
    [Fact]
    public void SlotKind_ResolvesKindTextFirstThenTheLegacyElementKey()
    {
        Assert.Equal(ImageStepSlotKind.Location, ReferenceBindingShape.SlotKindOf(ApprovedAsset("Location", 1, "Room")));
        Assert.Equal(ImageStepSlotKind.Body, ReferenceBindingShape.SlotKindOf(ApprovedAsset("Nonsense", 1, "Room", kind: "Body")));
        Assert.Null(ReferenceBindingShape.SlotKindOf(ApprovedAsset("Nonsense", 1, "Room")));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DreamGenClone.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root (holding DreamGenClone.sln) was not found.");
    }

    /// <summary>
    /// The EDIT surface reaches the separate-places branch too.
    ///
    /// <para>
    /// This is the guard against the fix going inert: the edit route builds its roles from
    /// <c>MediaEditReference</c>, and it carried no container at all until 2026-10-12 — so the operator's first-choice
    /// route (edit one trailer's front, add the other beside it) would have kept the old singular-room wording and the
    /// "do not replace it with the setting of any other reference image" exclusivity, whatever the generate route did.
    /// </para>
    /// </summary>
    [Fact]
    public void TheEditInstructionCarriesTheContainerSoItCanTellPlacesApart()
    {
        var instruction = FinalImagePromptPlanBuilder.BuildEditInstruction(
            "Add the other trailer beside this one, facing it.",
            [
                (ImageStepSlotKind.Location, "Couples Front", "trailer-couples", null),
                (ImageStepSlotKind.Location, "Other Front", "trailer-other", null)
            ]);

        // Source image occupies slot 1, so the references are <image2> and <image3>.
        Assert.Contains("<image2>", instruction, StringComparison.Ordinal);
        Assert.Contains("<image3>", instruction, StringComparison.Ordinal);
        Assert.Contains("is a SEPARATE PLACE in this frame", instruction, StringComparison.Ordinal);
        Assert.DoesNotContain("this image IS the room", instruction, StringComparison.Ordinal);
    }

    /// <summary>
    /// ...and the SAME container on the edit route still means one place seen twice, so a multi-view location edit is
    /// untouched by the branch above.
    /// </summary>
    [Fact]
    public void TheEditInstructionWithOneContainerKeepsTheSameRoomWording()
    {
        var instruction = FinalImagePromptPlanBuilder.BuildEditInstruction(
            "Show the back of this shed.",
            [
                (ImageStepSlotKind.Location, "Outdoor Front", "the-shed", null),
                (ImageStepSlotKind.Location, "Workbench Back", "the-shed", null)
            ]);

        Assert.Contains("this image IS the room", instruction, StringComparison.Ordinal);
        Assert.DoesNotContain("is a SEPARATE PLACE in this frame", instruction, StringComparison.Ordinal);
    }

    private static ReferenceApplicationSelection ApprovedAsset(
        string elementKey,
        int ordinal,
        string label,
        string? kind = null,
        string strategy = "NativeMultiReference",
        string? assetId = "6dd275caf4fc47bb9532c66ab2243727") => new()
    {
        ElementKey = elementKey,
        Kind = kind,
        Ordinal = ordinal,
        SemanticRole = "location continuity",
        Strategy = strategy,
        SceneAssetId = assetId,
        SceneAssetImageId = "4371dc7fd21d4344994ea533eca6a33a",
        SceneAssetVersion = 1,
        SceneAssetSha256 = "87B4001C66EEF105CEE7A4D260219A4942814A98D7186F846DE167B5D1E56B6C",
        ReferenceLabel = label
    };

    private static ReferenceApplicationSelection PackFace(int ordinal, string label) => new()
    {
        ElementKey = "Identity",
        Kind = nameof(ImageStepSlotKind.Face),
        Ordinal = ordinal,
        ActorKey = "de351eb3-69d3-421a-a762-79ae8ee183ed",
        SemanticRole = "character identity",
        Strategy = "NativeMultiReference",
        Source = nameof(ImageStepReferenceSourceKind.IdentityPackAsset),
        IdentityPackId = "2d13c667-a690-4b5a-992a-93359591aa41",
        ReferenceAssetId = FaceImage,
        ReferenceLabel = label
    };

    private static ReferenceApplicationSelection TextOnly(string elementKey, int ordinal) => new()
    {
        ElementKey = elementKey,
        Kind = elementKey,
        Ordinal = ordinal,
        SemanticRole = "character wardrobe",
        Strategy = "TextOnly"
    };

    /// <summary>A pack BUILD reference, so the two-image cases exercise both pack images rather than one twice.</summary>
    private static ReferenceApplicationSelection PackBuild(int ordinal, string label) => new()
    {
        ElementKey = "Body",
        Kind = nameof(ImageStepSlotKind.Body),
        Ordinal = ordinal,
        ActorKey = "de351eb3-69d3-421a-a762-79ae8ee183ed",
        SemanticRole = "character body",
        Strategy = "NativeMultiReference",
        Source = nameof(ImageStepReferenceSourceKind.IdentityPackAsset),
        IdentityPackId = "2d13c667-a690-4b5a-992a-93359591aa41",
        ReferenceAssetId = BuildImage,
        ReferenceLabel = label
    };

    /// <summary>
    /// The reported case end to end: face + build + location, numbered 1..3 in the order the render sends them, with
    /// the room named last because that is where it is sent.
    /// </summary>
    [Fact]
    public void TheReportedThreeReferenceCaseIsNumberedFaceBuildThenRoom()
    {
        var clause = ReferenceRoleClauses.ForPreprocessor(
        [
            PackFace(ordinal: 1, label: "Front"),
            PackBuild(ordinal: 2, label: "Front · Unclothed"),
            ApprovedAsset("Location", ordinal: 3, label: "Workbench Back")
        ]);

        var face = clause.IndexOf("<image1> (Front)", StringComparison.Ordinal);
        var build = clause.IndexOf("<image2> (Front · Unclothed)", StringComparison.Ordinal);
        var room = clause.IndexOf("<image3> (Workbench Back)", StringComparison.Ordinal);

        Assert.True(face >= 0 && build > face && room > build, clause);
        // The appended IMAGE-MODEL block states the room and stops there: the caption-writer's instructions do not
        // belong in the picture prompt (operator, 2026-10-22).
        var imagePrompt = ReferenceRoleClauses.AppendToImagePrompt("x", ReferenceRoleClauses.RolesFor([
            PackFace(ordinal: 1, label: "Front"),
            PackBuild(ordinal: 2, label: "Front · Unclothed"),
            ApprovedAsset("Location", ordinal: 3, label: "Workbench Back")
        ]));
        Assert.Contains("this image IS the room", imagePrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Frame the shot inside that room", imagePrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Name the setting only as its kind of place", imagePrompt, StringComparison.Ordinal);
    }
}
