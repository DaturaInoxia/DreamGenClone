using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-160: what an image analysis is ALLOWED to say, and how what it says reaches the prompt.
///
/// <para>
/// The whole feature is one promise — "the model tries first, the operator can overwrite" — and it only holds if the
/// rule runs in BOTH directions. An analysis must not be able to talk over a person, and it must be able to correct
/// itself. Everything below is that rule, plus the honesty requirement that the prompt must not present an analysis's
/// reading as something the operator said.
/// </para>
/// </summary>
public sealed class LocationAnalysisPrefillTests
{
    private static LocationImageAnalysis Analysis(params LocationAnchor[] anchors) => new(
        "Two trailers face each other across a mown yard, a shed between them at the back.",
        anchors,
        "SHA-ONE",
        new DateTime(2026, 10, 13, 17, 0, 0, DateTimeKind.Utc));

    private static ReferenceApplicationSelection LocationBinding(
        ReferenceDirections? directions = null,
        string assetId = "trailer-couples") => new()
    {
        ElementKey = "Location",
        Kind = nameof(ImageStepSlotKind.Location),
        Ordinal = 1,
        SemanticRole = "location continuity",
        Source = nameof(ImageStepReferenceSourceKind.ApprovedSceneAsset),
        Strategy = ReferenceStrategyResolver.IdentityNativeMultiReference,
        SceneAssetId = assetId,
        SceneAssetImageId = $"{assetId}-front",
        SceneAssetVersion = 1,
        SceneAssetSha256 = "SHA",
        ReferenceLabel = assetId,
        Directions = directions
    };

    /// <summary>The proposal names the features AND where they sit, which is the part a prompt cannot invent.</summary>
    [Fact]
    public void TheProposalCarriesTheDescriptionAndEachFeatureWithItsPosition()
    {
        var directions = ReferenceDirectionsPrefill.ForLocationAnalysis(Analysis(
            new LocationAnchor(LocationAnchorRole.Structure, "the trailer on the left", FrameHorizontal.Left, FrameDepth.Midground),
            new LocationAnchor(LocationAnchorRole.Deck, "the deck on the right-hand trailer", FrameHorizontal.Right),
            new LocationAnchor(LocationAnchorRole.Clothesline, "the rotary line in the centre")));

        Assert.NotNull(directions);
        Assert.Equal(ReferenceDirectionSource.Analysis, directions!.Source);
        Assert.Contains("Two trailers face each other", directions.Note, StringComparison.Ordinal);
        Assert.Contains("the trailer on the left (Left, Midground)", directions.Note, StringComparison.Ordinal);
        Assert.Contains("the deck on the right-hand trailer (Right)", directions.Note, StringComparison.Ordinal);
        Assert.Contains("the rotary line in the centre", directions.Note, StringComparison.Ordinal);

        // No side or facing: a place holds MANY features, so one side would be a guess — the very guess that put a
        // character on the wrong trailer.
        Assert.Null(directions.Side);
        Assert.Null(directions.Facing);
    }

    [Fact]
    public void NothingIsProposedWithoutAnAnalysis()
    {
        Assert.Null(ReferenceDirectionsPrefill.ForLocationAnalysis(null));
        Assert.Null(ReferenceDirectionsPrefill.Apply(null, null));
    }

    /// <summary>An empty reading proposes nothing rather than an empty statement.</summary>
    [Fact]
    public void AReadingThatSaysNothingProposesNothing()
    {
        var empty = new LocationImageAnalysis(string.Empty, [], "SHA-ONE", DateTime.UtcNow);
        Assert.Null(ReferenceDirectionsPrefill.ForLocationAnalysis(empty));
    }

    /// <summary>
    /// The model speaks first: a reference with no statement gets one.
    /// </summary>
    [Fact]
    public void AnAnalysisFillsAReferenceThatHasNoStatement()
    {
        var applied = ReferenceDirectionsPrefill.Apply(null, Analysis(
            new LocationAnchor(LocationAnchorRole.Deck, "the right-hand trailer's deck", FrameHorizontal.Right)));

        Assert.NotNull(applied);
        Assert.Contains("the right-hand trailer's deck (Right)", applied!.Note, StringComparison.Ordinal);
        Assert.Equal(ReferenceDirectionSource.Analysis, applied.Source);
    }

    /// <summary>
    /// ...and it may correct ITSELF, so a re-read of a changed image can update what an earlier read proposed.
    /// </summary>
    [Fact]
    public void AnAnalysisReplacesAnEarlierAnalysisProposal()
    {
        var firstAnalysis = Analysis(new LocationAnchor(LocationAnchorRole.Deck, "a deck on the left"));
        var secondAnalysis = Analysis(new LocationAnchor(
            LocationAnchorRole.Deck, "the deck on the right-hand trailer", FrameHorizontal.Right));

        var first = ReferenceDirectionsPrefill.ForLocationAnalysis(firstAnalysis);
        var applied = ReferenceDirectionsPrefill.Apply(first, secondAnalysis);

        Assert.Contains("the deck on the right-hand trailer", applied!.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("a deck on the left", applied.Note, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE ONE THAT MATTERS: the operator's own statement is never talked over, not by a re-read and not by a
    /// contradicting reading.
    /// </summary>
    [Fact]
    public void AnAnalysisNeverReplacesWhatTheOperatorWrote()
    {
        var operatorStatement = new ReferenceDirections(
            FrameHorizontal.Right, ReferenceFacing.Left, "Dean watching from the deck");

        var applied = ReferenceDirectionsPrefill.Apply(operatorStatement, Analysis(
            new LocationAnchor(LocationAnchorRole.Deck, "a deck on the left")));

        Assert.Same(operatorStatement, applied);
    }

    /// <summary>
    /// And the way a statement BECOMES the operator's is simply editing it — which is what the composer's controls do,
    /// because they build a fresh record whose source defaults to the operator.
    /// </summary>
    [Fact]
    public void EditingAProposalMakesItTheOperators()
    {
        var proposed = ReferenceDirectionsPrefill.ForLocationAnalysis(Analysis(
            new LocationAnchor(LocationAnchorRole.Deck, "a deck")));
        Assert.True(proposed!.MayBeReplacedByAnalysis);

        var edited = new ReferenceDirections(proposed.Side, proposed.Facing, "the deck on the far trailer");

        Assert.False(edited.MayBeReplacedByAnalysis);
        Assert.Same(edited, ReferenceDirectionsPrefill.Apply(edited, Analysis(
            new LocationAnchor(LocationAnchorRole.Deck, "something else entirely"))));
    }

    /// <summary>
    /// The prompt must not present an analysis's reading as the operator's words — and (2026-10-20) the PRE-PROCESSOR
    /// must not be handed the analysis at all, because the caption writer reproduces it verbatim. The image model still
    /// receives both, attributed correctly.
    /// </summary>
    [Fact]
    public void TheClauseAttributesAnAnalysisReadingToTheImageAndAStatementToTheOperator()
    {
        var analysisDirections = new ReferenceDirections(
            null, null, "the deck is on the right-hand trailer", ReferenceDirectionSource.Analysis);
        var fromAnalysis = ReferenceRoleClauses.ForPreprocessor(
        [
            LocationBinding(analysisDirections),
            LocationBinding()
        ]);

        // The caption writer is not given the machine reading: it copies it into the prose.
        Assert.DoesNotContain("What this image contains:", fromAnalysis, StringComparison.Ordinal);
        Assert.DoesNotContain("the deck is on the right-hand trailer", fromAnalysis, StringComparison.Ordinal);

        var fromOperator = ReferenceRoleClauses.ForPreprocessor(
        [
            LocationBinding(new ReferenceDirections(null, null, "the deck is on the right-hand trailer")),
            LocationBinding()
        ]);

        Assert.Contains("The operator says of it", fromOperator, StringComparison.Ordinal);
        Assert.DoesNotContain("What this image contains:", fromOperator, StringComparison.Ordinal);

        // The IMAGE MODEL is given the image itself; the analysis reading is not restated in prose either (2026-10-21).
        var forImageModel = string.Join(" ", new[] { LocationBinding(analysisDirections), LocationBinding() }
            .Select(binding => ReferenceRoleClauses.RolesFor([binding]))
            .SelectMany(roles => roles)
            .Select(role => ReferenceRoleClauses.BlockFor([role])));
        Assert.DoesNotContain("the deck is on the right-hand trailer", forImageModel, StringComparison.Ordinal);
    }

    /// <summary>An operator-sourced statement still names no tag of its own — the clause assigns that at render.</summary>
    [Fact]
    public void AnAnalysisSourcedStatementCarriesNoTag()
    {
        var clause = ReferenceRoleClauses.ForPreprocessor(
        [
            LocationBinding(new ReferenceDirections(null, null, "the deck is on the right-hand trailer"), "trailer-couples"),
            LocationBinding(null, "trailer-other")
        ]);

        var start = clause.IndexOf("The operator says of it:", StringComparison.Ordinal);
        Assert.True(start >= 0);

        // Bounded to the SENTENCE, not to a fixed number of characters: the block's next line begins with a tag, and a
        // window wide enough to reach it would fail for a reason that has nothing to do with the statement.
        var end = clause.IndexOf('"', clause.IndexOf('"', start) + 1);
        Assert.True(end > start);

        Assert.DoesNotContain("<image", clause[start..end], StringComparison.Ordinal);
        Assert.Contains("the deck is on the right-hand trailer", clause[start..end], StringComparison.Ordinal);
    }

    /// <summary>
    /// The reported gap, closed: a step holding ONLY the analysed location still carries the reading into the prompt.
    ///
    /// <para>
    /// Group `dfda210c` binds a location backdrop and nothing else, so the step sends ONE image — and with a single
    /// image a tag is forbidden, which used to mean the whole reference was silent and the reading was inert until a
    /// face or a body was bound beside it (2026-10-13). The clause now describes that image in the same words minus the
    /// number: no tag anywhere, and the block says so, so the model is never left to guess one.
    /// </para>
    /// </summary>
    [Fact]
    public void ALocationOnlyStepCarriesTheReadingIntoThePromptUntagged()
    {
        var directions = ReferenceDirectionsPrefill.Apply(null, Analysis(
            new LocationAnchor(LocationAnchorRole.Deck, "the deck on the right trailer", FrameHorizontal.Right, FrameDepth.Foreground),
            new LocationAnchor(LocationAnchorRole.Clothesline, "the clothesline in the centre")));

        var bindings = new[] { LocationBinding(directions) };

        var block = ReferenceRoleClauses.BlockFor(ReferenceRoleClauses.RolesFor(bindings));

        // The analysis READING is deliberately not emitted (2026-10-21): the operator reads it in the picker and the
        // image model is given the image. Stating it in prose is what the compiled prompt copied verbatim.
        Assert.DoesNotContain("What this image contains", block, StringComparison.Ordinal);
        Assert.DoesNotContain("the deck on the right trailer", block, StringComparison.Ordinal);
        Assert.Contains("this image IS the room", block, StringComparison.Ordinal);
        Assert.DoesNotContain("<image", block, StringComparison.Ordinal);
    }
}
