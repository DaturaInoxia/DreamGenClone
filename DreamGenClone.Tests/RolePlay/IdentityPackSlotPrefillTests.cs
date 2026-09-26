using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The pack-reference decision and the step pre-fill built on it.
///
/// Two things matter here and are pinned: the decision is a MATCH (newest approved image for the angle, and for the
/// body also the state — a reference image carries its state with it), and the pre-fill fills exactly the slots the
/// blueprint declares in the shape the host asked for — face only, body only, or both.
/// </summary>
public sealed class IdentityPackSlotPrefillTests
{
    private const string PackId = "becky-pack-1";

    private static readonly ImageStepActor Becky = new("becky", "Becky");

    // ------------------------------------------------------------------ the match itself

    [Fact]
    public void ResolveFace_PrefersTheNewestApprovedReferenceForTheAngle()
    {
        var assets = new[]
        {
            Face("older", SceneImageReferenceFaceView.Front, createdUtc: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)),
            Face("newer", SceneImageReferenceFaceView.Front, createdUtc: new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc)),
            Face("elsewhere", SceneImageReferenceFaceView.ProfileLeft, createdUtc: new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc))
        };

        Assert.Equal("newer", IdentityPackReferenceResolver.ResolveFace(assets, SceneImageReferenceFaceView.Front)!.Id);
    }

    /// <summary>
    /// An unapproved reference is not a reference: the pack is the source of truth for what has been QC'd, and a draft
    /// image reaching a render would put a face nobody accepted into the training set.
    /// </summary>
    [Fact]
    public void ResolveFace_IgnoresUnapprovedReferences()
    {
        var assets = new[] { Face("draft", SceneImageReferenceFaceView.Front, approved: false) };

        Assert.Null(IdentityPackReferenceResolver.ResolveFace(assets, SceneImageReferenceFaceView.Front));
    }

    /// <summary>
    /// The match does NOT fall back to another angle. A frontal reference conditioning a profile cell trains the token
    /// on a face that does not match the view it was learned from.
    /// </summary>
    [Fact]
    public void ResolveFace_ReturnsNullForAnAngleThePackDoesNotServe()
    {
        var assets = new[] { Face("front", SceneImageReferenceFaceView.Front) };

        Assert.Null(IdentityPackReferenceResolver.ResolveFace(assets, SceneImageReferenceFaceView.ProfileRight));
    }

    /// <summary>A body reference carries its wardrobe state, so the state is part of the match, not a hint.</summary>
    [Fact]
    public void ResolveBody_MatchesTheStateAsWellAsTheAngle()
    {
        var assets = new[]
        {
            Body("clothed", SceneImageReferenceBodyView.Front, SceneImageReferenceBodyState.Clothed),
            Body("unclothed", SceneImageReferenceBodyView.Front, SceneImageReferenceBodyState.Unclothed)
        };

        Assert.Equal(
            "unclothed",
            IdentityPackReferenceResolver.ResolveBody(
                assets, SceneImageReferenceBodyView.Front, SceneImageReferenceBodyState.Unclothed)!.Id);
    }

    /// <summary>A face reference is not a body reference, however well its view matches.</summary>
    [Fact]
    public void ResolveBody_IgnoresFaceReferences()
    {
        var assets = new[] { Face("front-face", SceneImageReferenceFaceView.Front) };

        Assert.Null(IdentityPackReferenceResolver.ResolveBody(
            assets, SceneImageReferenceBodyView.Front, SceneImageReferenceBodyState.Clothed));
    }

    /// <summary>What a picker may offer is what the pack can actually serve, so it never shows a dead choice.</summary>
    [Fact]
    public void AvailableViews_AreOnlyTheAnglesThePackActuallyHas()
    {
        var assets = new[]
        {
            Face("front", SceneImageReferenceFaceView.Front),
            Face("half", SceneImageReferenceFaceView.ThreeQuarterLeft),
            Body("body", SceneImageReferenceBodyView.Back, SceneImageReferenceBodyState.Clothed)
        };

        Assert.Equal(
            [SceneImageReferenceFaceView.Front, SceneImageReferenceFaceView.ThreeQuarterLeft],
            IdentityPackReferenceResolver.AvailableFaceViews(assets));
        Assert.Equal(
            [(SceneImageReferenceBodyView.Back, SceneImageReferenceBodyState.Clothed)],
            IdentityPackReferenceResolver.AvailableBodies(assets));
    }

    // ------------------------------------------------------------------ the three shapes

    /// <summary>The close-up shape: a face and nothing else.</summary>
    [Fact]
    public void For_FaceOnly_FillsOnlyTheFaceSlot()
    {
        var bindings = IdentityPackSlotPrefill.For(
            Blueprint(),
            PackId,
            Pack(),
            IdentityPackSlotPrefill.Request.FaceOnly(Becky, SceneImageReferenceFaceView.Front));

        var binding = Assert.Single(bindings);
        Assert.Equal(nameof(ImageStepSlotKind.Face), binding.Kind);
        Assert.Equal("becky", binding.ActorKey);
        Assert.Equal(PackId, binding.IdentityPackId);
        Assert.Equal("front", binding.ReferenceAssetId);
        Assert.Equal(ImageStepReferenceSourceKind.IdentityPackAsset.ToString(), binding.Source);
        Assert.Equal(1, binding.Ordinal ?? 0);
        Assert.True(binding.SuppliesImage);
    }

    /// <summary>The build shape: a body and nothing else.</summary>
    [Fact]
    public void For_BodyOnly_FillsOnlyTheBodySlot()
    {
        var bindings = IdentityPackSlotPrefill.For(
            Blueprint(),
            PackId,
            Pack(),
            IdentityPackSlotPrefill.Request.BodyOnly(
                Becky, SceneImageReferenceBodyView.Front, SceneImageReferenceBodyState.Clothed));

        var binding = Assert.Single(bindings);
        Assert.Equal(nameof(ImageStepSlotKind.Body), binding.Kind);
        Assert.Equal("clothed", binding.ReferenceAssetId);
    }

    /// <summary>The full-frame shape, in FRAME ORDER: the face anchors the frame before the build follows it.</summary>
    [Fact]
    public void For_FaceAndBody_FillsBothInFrameOrder()
    {
        var bindings = IdentityPackSlotPrefill.For(
            Blueprint(),
            PackId,
            Pack(),
            IdentityPackSlotPrefill.Request.FaceAndBody(
                Becky,
                SceneImageReferenceFaceView.ProfileLeft,
                SceneImageReferenceBodyView.ProfileLeft,
                SceneImageReferenceBodyState.Clothed));

        Assert.Equal([nameof(ImageStepSlotKind.Face), nameof(ImageStepSlotKind.Body)],
            bindings.Select(binding => binding.Kind));
        Assert.Equal([1, 2], bindings.Select(binding => binding.Ordinal ?? 0));
        Assert.Equal("profile-face", bindings[0].ReferenceAssetId);
        Assert.Equal("profile-body", bindings[1].ReferenceAssetId);
    }

    /// <summary>
    /// A requested element the blueprint does not declare is REFUSED, not dropped: the planner drops undeclared
    /// assignments, so the reference would vanish silently and the render would not be the character.
    /// </summary>
    [Fact]
    public void For_RefusesAnElementTheStepHasNoSlotFor()
    {
        var error = Assert.Throws<InvalidOperationException>(() => IdentityPackSlotPrefill.For(
            Blueprint(includeBody: false),
            PackId,
            Pack(),
            IdentityPackSlotPrefill.Request.FaceAndBody(
                Becky,
                SceneImageReferenceFaceView.Front,
                SceneImageReferenceBodyView.Front,
                SceneImageReferenceBodyState.Clothed)));

        Assert.Contains("no Body slot", error.Message, StringComparison.Ordinal);
        Assert.Contains("becky", error.Message, StringComparison.Ordinal);
    }

    /// <summary>The angle the step depicts decides the refusal, so the operator is told which view to shoot.</summary>
    [Fact]
    public void For_NamesTheMissingAngleRatherThanSubstitutingAnother()
    {
        var error = Assert.Throws<InvalidOperationException>(() => IdentityPackSlotPrefill.For(
            Blueprint(),
            PackId,
            Pack(),
            IdentityPackSlotPrefill.Request.FaceOnly(Becky, SceneImageReferenceFaceView.ProfileRight)));

        Assert.Contains(PackId, error.Message, StringComparison.Ordinal);
        Assert.Contains("ProfileRight", error.Message, StringComparison.Ordinal);
        Assert.Contains("Faces tab", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void For_RequiresAPack()
    {
        Assert.Throws<InvalidOperationException>(() => IdentityPackSlotPrefill.For(
            Blueprint(),
            "   ",
            Pack(),
            IdentityPackSlotPrefill.Request.FaceOnly(Becky, SceneImageReferenceFaceView.Front)));
    }

    // ------------------------------------------------------------------ fixtures

    /// <summary>A step declaring the slots the face/body shapes need, addressed to Becky.</summary>
    private static ImageStepBlueprint Blueprint(bool includeBody = true)
    {
        var slots = new List<ImageStepSlotBlueprint>
        {
            new(ImageStepSlotKind.Face, ImageStepSlotPrefill.PackCanonicalFace,
                [ImageStepReferenceSourceKind.IdentityPackAsset], Becky.ActorKey)
        };
        if (includeBody)
        {
            slots.Add(new ImageStepSlotBlueprint(ImageStepSlotKind.Body, ImageStepSlotPrefill.PackCanonicalBody,
                [ImageStepReferenceSourceKind.IdentityPackAsset], Becky.ActorKey));
        }

        return new ImageStepBlueprint(
            ImageStepKind.Compose, "Composition", ImageStepSourceMode.None, slots, ImageStepPersistenceKind.SceneImage);
    }

    private static IReadOnlyList<SceneImageReferenceAsset> Pack() =>
    [
        Face("front", SceneImageReferenceFaceView.Front),
        Face("profile-face", SceneImageReferenceFaceView.ProfileLeft),
        Body("clothed", SceneImageReferenceBodyView.Front, SceneImageReferenceBodyState.Clothed),
        Body("profile-body", SceneImageReferenceBodyView.ProfileLeft, SceneImageReferenceBodyState.Clothed)
    ];

    private static SceneImageReferenceAsset Face(
        string id, SceneImageReferenceFaceView view, bool approved = true, DateTime? createdUtc = null) => new()
    {
        Id = id,
        IdentityPackId = PackId,
        AssetKind = SceneImageReferenceAssetKind.Face,
        FaceView = view,
        IsApproved = approved,
        CreatedUtc = createdUtc ?? new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc)
    };

    private static SceneImageReferenceAsset Body(
        string id, SceneImageReferenceBodyView view, SceneImageReferenceBodyState state, bool approved = true) => new()
    {
        Id = id,
        IdentityPackId = PackId,
        AssetKind = SceneImageReferenceAssetKind.FullBody,
        BodyView = view,
        BodyState = state,
        IsApproved = approved,
        CreatedUtc = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc)
    };
}
