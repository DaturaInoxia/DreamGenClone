using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-157 — the role clause when a frame-space blocking is supplied.
///
/// <para>
/// The reported failure was not that references were dropped: all four were sent with correct tags. It was that two
/// characters' faces were both labelled by their pack VIEW ("Front · Clothed"), so nothing in the prompt said which
/// person <c>&lt;image1&gt;</c> was or where either of them stood — and the model resolved that ambiguity the same way
/// every time (the first face wins). These tests pin the disambiguation, and pin that a caller WITHOUT a blocking gets
/// byte-identical output to the old form, so deriving a frame is what changes a render and nothing else does.
/// </para>
/// </summary>
public sealed class ReferenceRoleClauseBlockingTests
{
    private static ReferenceApplicationSelection Binding(ImageStepSlotKind kind, string? actorKey, string label, int ordinal) => new()
    {
        ElementKey = kind.ToString(),
        SemanticRole = kind.ToString(),
        Kind = kind.ToString(),
        Strategy = "NativeMultiReference",
        ActorKey = actorKey,
        Ordinal = ordinal,
        SceneAssetId = $"asset-{label}",
        SceneAssetImageId = $"image-{label}",
        ReferenceLabel = label
    };

    /// <summary>Two characters whose packs label their fronts identically — the collision the operator hit.</summary>
    private static IReadOnlyList<ReferenceApplicationSelection> TwoCharacters() =>
    [
        Binding(ImageStepSlotKind.Face, "p0", "Front · Clothed", 1),
        Binding(ImageStepSlotKind.Body, "p0", "Body · Front", 2),
        Binding(ImageStepSlotKind.Face, "p1", "Front · Clothed", 3),
        Binding(ImageStepSlotKind.Location, null, "The shed", 4)
    ];

    private static SceneBlocking Blocking() => new(
        "1",
        [
            new SceneBlockingSubject(
                "p0", new FramePosition(FrameHorizontal.Center, FrameDepth.Foreground), "the person in the CENTER in the foreground"),
            new SceneBlockingSubject(
                "p1", new FramePosition(FrameHorizontal.Left, FrameDepth.Background), "the person on the LEFT in the background")
        ],
        new SceneBlockingCamera("eye level", "medium shot", null),
        ["p0", "p1"],
        "A wide two-shot.");

    [Fact]
    public void RolesFor_CarriesTheBindingActorKeyAsTheSubjectKey()
    {
        var roles = ReferenceRoleClauses.RolesFor(TwoCharacters());

        Assert.Equal("p0", roles[0].SubjectKey);
        Assert.Equal("p0", roles[1].SubjectKey);
        Assert.Equal("p1", roles[2].SubjectKey);
        // A location is scene-level and names no actor, so it has no subject key to join on.
        Assert.Null(roles[3].SubjectKey);
    }

    [Fact]
    public void BlockFor_WithBlocking_NamesEachPersonByPosition()
    {
        var roles = ReferenceRoleClauses.RolesFor(TwoCharacters());

        var block = ReferenceRoleClauses.BlockFor(roles, ReferenceRoleClauses.LeadingImageCount.Generate, Blocking());

        // The two faces no longer read identically: each is tied to a person and a place in the frame.
        Assert.Contains("is the person in the CENTER", block, StringComparison.Ordinal);
        Assert.Contains("is the person on the LEFT", block, StringComparison.Ordinal);

        var lines = block.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var centre = Assert.Single(lines, line => line.Contains("is the person in the CENTER", StringComparison.Ordinal));
        var left = Assert.Single(lines, line => line.Contains("is the person on the LEFT", StringComparison.Ordinal));
        Assert.StartsWith("<image1>", centre, StringComparison.Ordinal);
        Assert.StartsWith("<image3>", left, StringComparison.Ordinal);

        // The second reference of the same person refers BACK to them instead of claiming a second position.
        var body = Assert.Single(lines, line => line.Contains("supplies that same person's build", StringComparison.Ordinal));
        Assert.StartsWith("<image2>", body, StringComparison.Ordinal);
        Assert.Equal(1, lines.Count(line => line.Contains("in the CENTER", StringComparison.Ordinal)));
    }

    [Fact]
    public void BlockFor_WithBlocking_LeavesASceneLevelReferenceAsTheRoom()
    {
        var roles = ReferenceRoleClauses.RolesFor(TwoCharacters());

        var block = ReferenceRoleClauses.BlockFor(roles, ReferenceRoleClauses.LeadingImageCount.Generate, Blocking());

        var lines = block.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var location = Assert.Single(lines, line => line.StartsWith("<image4>", StringComparison.Ordinal));
        // A room is not a person: it keeps its own sentence and is never given a frame position.
        Assert.Contains("is the LOCATION reference", location, StringComparison.Ordinal);
        Assert.DoesNotContain("the person", location, StringComparison.Ordinal);
    }

    [Fact]
    public void BlockFor_WithoutBlocking_KeepsTheEstablishedForm()
    {
        var roles = ReferenceRoleClauses.RolesFor(TwoCharacters());

        var withBlocking = ReferenceRoleClauses.BlockFor(
            roles, ReferenceRoleClauses.LeadingImageCount.Generate, Blocking());
        var without = ReferenceRoleClauses.BlockFor(roles, ReferenceRoleClauses.LeadingImageCount.Generate);

        // Deriving a frame is what changes the clause; a caller that has not derived one is unaffected.
        Assert.DoesNotContain("is the person in the CENTER", without, StringComparison.Ordinal);
        Assert.DoesNotContain("supplies that same person's", without, StringComparison.Ordinal);
        Assert.Contains("Front · Clothed", without, StringComparison.Ordinal);
        Assert.Contains("is the FACE reference", without, StringComparison.Ordinal);
        Assert.NotEqual(withBlocking, without);
    }

    [Fact]
    public void BlockFor_WithBlocking_FallsBackWhenTheFrameDoesNotPlaceTheSubject()
    {
        var roles = ReferenceRoleClauses.RolesFor(TwoCharacters());

        // A frame that places neither character must not lend one of them a position: an invented placement is
        // indistinguishable from a chosen one in the finished image.
        var otherFrame = new SceneBlocking(
            "1",
            [new SceneBlockingSubject(
                "someone-else", new FramePosition(FrameHorizontal.Right, FrameDepth.Background), "the person on the RIGHT")],
            new SceneBlockingCamera("eye level", "medium shot", null),
            ["someone-else"],
            string.Empty);

        var block = ReferenceRoleClauses.BlockFor(roles, ReferenceRoleClauses.LeadingImageCount.Generate, otherFrame);

        Assert.DoesNotContain("the person on the RIGHT", block, StringComparison.Ordinal);
        Assert.Contains("is the FACE reference", block, StringComparison.Ordinal);
    }

    [Fact]
    public void BlockFor_WithBlocking_KeepsTagsAndSendOrder()
    {
        var roles = ReferenceRoleClauses.RolesFor(TwoCharacters());

        var block = ReferenceRoleClauses.BlockFor(roles, ReferenceRoleClauses.LeadingImageCount.Generate, Blocking());

        // The framing changes the LABELLING only: numbering still follows send order, one tag per image.
        Assert.Contains("<image1>", block, StringComparison.Ordinal);
        Assert.Contains("<image4>", block, StringComparison.Ordinal);
        Assert.DoesNotContain("<image5>", block, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ T5: one vocabulary on both halves

    [Fact]
    public void ForPreprocessor_WithBlocking_UsesTheSamePositionsAsTheAppendedBlock()
    {
        var bindings = TwoCharacters();

        var preprocessor = ReferenceRoleClauses.ForPreprocessor(bindings, Blocking());
        var appended = ReferenceRoleClauses.BlockFor(
            ReferenceRoleClauses.RolesFor(bindings), ReferenceRoleClauses.LeadingImageCount.Generate, Blocking());

        // The prose the pre-processor writes and the block the render appends must name the same place for the same
        // person; two vocabularies would let the prose describe a frame the clause then contradicts.
        Assert.Contains("for the person in the CENTER", preprocessor, StringComparison.Ordinal);
        Assert.Contains("for that same person", preprocessor, StringComparison.Ordinal);
        Assert.Contains("<image1>", preprocessor, StringComparison.Ordinal);
        Assert.Contains("<image3>", preprocessor, StringComparison.Ordinal);
        Assert.Contains("is the person on the LEFT", appended, StringComparison.Ordinal);
    }

    [Fact]
    public void ForPreprocessor_WithoutBlocking_KeepsTheViewLabelForm()
    {
        var preprocessor = ReferenceRoleClauses.ForPreprocessor(TwoCharacters());

        Assert.Contains("Front · Clothed", preprocessor, StringComparison.Ordinal);
        Assert.DoesNotContain("is the person in the CENTER", preprocessor, StringComparison.Ordinal);
    }
}
