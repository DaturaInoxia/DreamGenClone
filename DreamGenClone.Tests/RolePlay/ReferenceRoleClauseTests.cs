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
    private const string FaceImage = "282f5b91ba7d4dba85140ae006aa06ee";
    private const string BuildImage = "37f0fa3f2dc3478eb47fb253beb1a104";

    /// <summary>
    /// THE ONE THAT MATTERS. The planned order and the sent order are not the same list: the render resolves
    /// pack-supplied images first (a pack image lives in its own store) and the approved-asset references after, so a
    /// step that binds a LOCATION at ordinal 1 and a pack FACE at ordinal 2 still sends the face FIRST. Numbering a
    /// clause from the planned order would name the room <c>&lt;image1&gt;</c> while the model received the face in
    /// slot 1 — every tag after the first would point at the wrong image, and nothing would fail.
    /// </summary>
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
                (ImageStepSlotKind.Face, "Front"),
                (ImageStepSlotKind.Location, "Workbench Back")
            ]);

        var faceTag = instruction.IndexOf("<image2>", StringComparison.Ordinal);
        var locationTag = instruction.IndexOf("<image3>", StringComparison.Ordinal);
        Assert.True(faceTag >= 0, "the first edit reference must be <image2>: the source image is slot 1");
        Assert.True(locationTag > faceTag, "the second edit reference must be <image3>");
        Assert.DoesNotContain("<image1>", instruction, StringComparison.Ordinal);
        Assert.StartsWith("replace the man with the woman", instruction, StringComparison.Ordinal);
    }

    /// <summary>One reference on an edit gets no tags either — the same N = 1 rule as a render.</summary>
    [Fact]
    public void AnEditInstructionWithOneReferenceIsLeftAlone()
    {
        var instruction = ReferenceRoleClauses.AppendToEditInstruction(
            "replace the man with the woman",
            [(ImageStepSlotKind.Location, "Workbench Back")]);

        Assert.Equal("replace the man with the woman", instruction);
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

        Assert.Contains("ReferenceRoleClauses.AppendToEditInstruction(", handler, StringComparison.Ordinal);
        Assert.Contains("parts.References.Select(reference => (reference.SlotKind, reference.Description))", handler, StringComparison.Ordinal);
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
    /// The room is stated as the room, and explicitly protected from being replaced by another reference's setting —
    /// which is the failure that started this: a studio-lit identity plate carried its own background over the shed.
    /// </summary>
    [Fact]
    public void TheLocationLineSaysTheImageIsTheRoom()
    {
        var clause = ReferenceRoleClauses.ForPreprocessor(
        [
            PackFace(ordinal: 1, label: "Front"),
            ApprovedAsset("Location", ordinal: 2, label: "Workbench Back")
        ]);

        Assert.Contains("this image IS the room", clause, StringComparison.Ordinal);
        Assert.Contains("Do not invent a different room", clause, StringComparison.Ordinal);
    }

    /// <summary>
    /// A person plate must not donate its background or its lighting. Both identity plates in the reported case were
    /// studio shots, and the tagless render drifted +24 brighter than the shed it was supposed to be inside.
    /// </summary>
    [Fact]
    public void APersonPlateIsToldNotToDonateItsBackgroundOrLighting()
    {
        var clause = ReferenceRoleClauses.ForPreprocessor(
        [
            PackFace(ordinal: 1, label: "Front"),
            ApprovedAsset("Location", ordinal: 2, label: "Workbench Back")
        ]);

        Assert.Contains("Do not carry over its plain studio background or its studio lighting", clause, StringComparison.Ordinal);
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

        Assert.StartsWith("A nude woman on a workbench, 35mm.", composed, StringComparison.Ordinal);
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

    private static ReferenceApplicationSelection ApprovedAsset(
        string elementKey,
        int ordinal,
        string label,
        string? kind = null,
        string strategy = "NativeMultiReference") => new()
    {
        ElementKey = elementKey,
        Kind = kind,
        Ordinal = ordinal,
        SemanticRole = "location continuity",
        Strategy = strategy,
        SceneAssetId = "6dd275caf4fc47bb9532c66ab2243727",
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
        Assert.Contains(
            "Frame the shot inside that room",
            ReferenceRoleClauses.AppendToImagePrompt("x", ReferenceRoleClauses.RolesFor([
                PackFace(ordinal: 1, label: "Front"),
                PackBuild(ordinal: 2, label: "Front · Unclothed"),
                ApprovedAsset("Location", ordinal: 3, label: "Workbench Back")
            ])),
            StringComparison.Ordinal);
    }
}
