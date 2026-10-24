using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-159: what the OPERATOR says a reference image is doing — which side it sits on, which way it faces, and their own
/// note — reaching the prompt.
///
/// <para>
/// The point of the whole feature is one property: the statement is ANCHORED TO THE REFERENCE, never to a numbered tag.
/// It is emitted at RENDER, by the same code that assigns the <c>&lt;imageN&gt;</c>, so adding another reference moves
/// the tag without moving the sentence. That is what makes it safe to write in the UI, and — because the compile step
/// never sees this block — safe from being erased by Generate Prompt, which is the defect that started this
/// (2026-10-13: the operator had to overwrite the compiled prompt to place two trailers).
/// </para>
/// </summary>
public sealed class ReferenceDirectionsTests
{
    private static ReferenceApplicationSelection ApprovedAsset(
        string elementKey,
        int ordinal,
        string label,
        string assetId,
        ReferenceDirections? directions = null) => new()
    {
        ElementKey = elementKey,
        Kind = nameof(ImageStepSlotKind.Location),
        Ordinal = ordinal,
        SemanticRole = "location continuity",
        Source = nameof(ImageStepReferenceSourceKind.ApprovedSceneAsset),
        Strategy = ReferenceStrategyResolver.IdentityNativeMultiReference,
        SceneAssetId = assetId,
        SceneAssetImageId = $"{assetId}-image",
        SceneAssetVersion = 1,
        SceneAssetSha256 = "SHA",
        ReferenceLabel = label,
        Directions = directions
    };

    /// <summary>
    /// The operator's statement reaches the clause, is emitted ALONGSIDE the automatic sentence, and names no tag of
    /// its own — the tag stays the clause's business, assigned from the send order.
    /// </summary>
    [Fact]
    public void TheOperatorsStatementIsEmittedBesideTheAutomaticSentenceAndNamesNoTag()
    {
        var clause = ReferenceRoleClauses.ForPreprocessor(
        [
            ApprovedAsset("Location", 1, "Couples Front", "trailer-couples", new ReferenceDirections(
                FrameHorizontal.Left,
                ReferenceFacing.Right,
                "the far trailer, part hidden by the pines")),
            ApprovedAsset("Location", 2, "Other Front", "trailer-other")
        ]);

        // The automatic sentence is still there: the operator ADDS to it and cannot silently undo it.
        Assert.Contains("is one of the PLACES in this frame", clause, StringComparison.Ordinal);

        // ...and their own words are stated as facts about this image.
        Assert.Contains("The operator places it on the LEFT of the frame, facing RIGHT.", clause, StringComparison.Ordinal);
        Assert.Contains("The operator says of it: \"the far trailer, part hidden by the pines\".", clause, StringComparison.Ordinal);

        // The statement itself carries no number: a number here is exactly the drift this design exists to prevent.
        var start = clause.IndexOf("The operator places it", StringComparison.Ordinal);
        Assert.True(start >= 0);
        Assert.DoesNotContain("<image", clause[start..(start + 120)], StringComparison.Ordinal);
    }

    /// <summary>
    /// A binding the operator has not directed emits nothing extra, so every render that was already correct is byte
    /// for byte what it was.
    /// </summary>
    [Fact]
    public void NoStatementIsEmittedWhenTheOperatorSaidNothing()
    {
        var clause = ReferenceRoleClauses.ForPreprocessor(
        [
            ApprovedAsset("Location", 1, "Couples Front", "trailer-couples"),
            ApprovedAsset("Location", 2, "Other Front", "trailer-other")
        ]);

        Assert.DoesNotContain("The operator", clause, StringComparison.Ordinal);
    }

    /// <summary>An empty record and a null one mean the same thing, so neither can emit a sentence.</summary>
    [Fact]
    public void AnEmptyStatementIsTheSameAsNoStatement()
    {
        Assert.True(ReferenceDirections.Empty.IsEmpty);
        Assert.Equal(string.Empty, StatementOf(new ReferenceDirections(null, ReferenceFacing.Unknown, "   ")));

        var oneAxis = StatementOf(new ReferenceDirections(FrameHorizontal.Right, null, null));
        Assert.Contains("on the RIGHT", oneAxis, StringComparison.Ordinal);
        Assert.DoesNotContain("facing", oneAxis, StringComparison.Ordinal);
    }

    /// <summary>The note alone is a valid statement — the free text is not a decoration on the two dropdowns.</summary>
    [Fact]
    public void ANoteOnItsOwnIsStillStated()
    {
        var statement = StatementOf(new ReferenceDirections(null, null, "seen from the fence line"));

        Assert.Contains("seen from the fence line", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("places it", statement, StringComparison.Ordinal);
    }

    /// <summary>
    /// The tag a reference will be SENT as, which is what the composer now shows beside the name at bind time.
    ///
    /// <para>
    /// Two rules are pinned here because both would be silently wrong if a caller derived them itself: tags exist only
    /// when two or more images are sent (a single image gets none), and an EDIT numbers from the image AFTER its source.
    /// </para>
    /// </summary>
    [Fact]
    public void TagsForNumbersInSendOrderAndOnlyWhenTwoImagesAreSent()
    {
        var first = ApprovedAsset("Location", 1, "Couples Front", "trailer-couples");
        var second = ApprovedAsset("Location", 2, "Other Front", "trailer-other");

        var both = ReferenceRoleClauses.TagsFor([first, second]);
        Assert.Equal("<image1>", both[first]);
        Assert.Equal("<image2>", both[second]);

        // One image: no tags at all, because the model receives none.
        Assert.Empty(ReferenceRoleClauses.TagsFor([first]));

        // An edit's source image occupies slot 1, so its first reference is <image2>.
        var asEdit = ReferenceRoleClauses.TagsFor([first, second], ReferenceRoleClauses.LeadingImageCount.Edit);
        Assert.Equal("<image2>", asEdit[first]);
        Assert.Equal("<image3>", asEdit[second]);
    }

    /// <summary>
    /// The plan carries the operator's statement onto the binding, so it survives every re-plan the composer performs.
    /// A field that lived only on the binding would be dropped the next time the operator touched another control.
    /// </summary>
    [Fact]
    public void PlanningCarriesTheOperatorsStatementOntoTheBinding()
    {
        var directions = new ReferenceDirections(FrameHorizontal.Left, ReferenceFacing.Right, "tucked behind the shed");
        var blueprint = ImageStepBlueprintFactory.ForAssetCreate(null);

        var bindings = ReferenceSlotPlanner.Plan(
            blueprint,
            [
                new ImageStepSlotAssignment(ImageStepSlotKind.Location, null, new ImageStepSlotSource(
                    ImageStepReferenceSourceKind.ApprovedSceneAsset,
                    ReferenceStrategyResolver.IdentityNativeMultiReference,
                    SceneAssetId: "trailer-couples",
                    SceneAssetImageId: "trailer-couples-front",
                    SceneAssetVersion: 1,
                    SceneAssetSha256: "SHA",
                    ReferenceLabel: "Couples Front",
                    Directions: directions))
            ],
            maxReferences: 10);

        var binding = Assert.Single(bindings);
        Assert.Equal(directions, binding.Directions);
    }

    /// <summary>One reference's statement, as the clause emits it (everything after the automatic role sentence).</summary>
    private static string StatementOf(ReferenceDirections directions)
    {
        var clause = ReferenceRoleClauses.ForPreprocessor(
        [
            ApprovedAsset("Location", 1, "Couples Front", "trailer-couples", directions),
            ApprovedAsset("Location", 2, "Other Front", "trailer-other")
        ]);

        var start = clause.IndexOf("The operator", StringComparison.Ordinal);
        return start < 0 ? string.Empty : clause[start..].Split('\n')[0];
    }
}
