using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The body-view derivation chain, in one place. These rules are load-bearing in an expensive way — a wrong base does
/// not throw, it produces a plausible image of a slightly different body, which is the defect the whole reference set
/// exists to prevent — so every cell of the matrix is asserted rather than only the one that changed.
/// </summary>
public sealed class BodyViewDerivationTests
{
    private static CharacterIdentityBodyViewKey Key(
        SceneImageReferenceBodyState state, SceneImageReferenceBodyView view)
        => CharacterIdentityBodyViewKey.Canonical(state, view);

    /// <summary>
    /// The change requested 2026-10-10: an unclothed profile is RENDERED from its same-side three-quarter, so the
    /// render is shown the body already most of the way turned instead of being asked to reach edge-on in one step
    /// from the front.
    /// </summary>
    [Theory]
    [InlineData(SceneImageReferenceBodyView.ProfileLeft, SceneImageReferenceBodyView.ThreeQuarterLeft)]
    [InlineData(SceneImageReferenceBodyView.ProfileRight, SceneImageReferenceBodyView.ThreeQuarterRight)]
    public void AnUnclothedProfile_RendersFromItsOwnSideThreeQuarter(
        SceneImageReferenceBodyView profile, SceneImageReferenceBodyView expected)
    {
        var baseKey = BodyViewDerivation.RenderBaseFor(Key(SceneImageReferenceBodyState.Unclothed, profile));

        Assert.Equal(Key(SceneImageReferenceBodyState.Unclothed, expected), baseKey);
    }

    /// <summary>
    /// A CLOTHED profile keeps the front (operator decision 2026-10-10: only the unclothed set changes). The clothed
    /// set was measured working from the front and is left exactly as it was.
    /// </summary>
    [Theory]
    [InlineData(SceneImageReferenceBodyView.ProfileLeft)]
    [InlineData(SceneImageReferenceBodyView.ProfileRight)]
    public void AClothedProfile_StillRendersFromTheFront(SceneImageReferenceBodyView profile)
    {
        var baseKey = BodyViewDerivation.RenderBaseFor(Key(SceneImageReferenceBodyState.Clothed, profile));

        Assert.Equal(Key(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front), baseKey);
    }

    [Theory]
    [InlineData(SceneImageReferenceBodyState.Clothed)]
    [InlineData(SceneImageReferenceBodyState.Unclothed)]
    public void TheThreeQuartersAndTheBack_RenderFromTheFrontOfTheirOwnState(SceneImageReferenceBodyState state)
    {
        foreach (var view in new[]
                 {
                     SceneImageReferenceBodyView.ThreeQuarterLeft,
                     SceneImageReferenceBodyView.ThreeQuarterRight,
                     SceneImageReferenceBodyView.Back
                 })
        {
            Assert.Equal(
                Key(state, SceneImageReferenceBodyView.Front),
                BodyViewDerivation.RenderBaseFor(Key(state, view)));
        }
    }

    [Theory]
    [InlineData(SceneImageReferenceBodyState.Clothed)]
    [InlineData(SceneImageReferenceBodyState.Unclothed)]
    public void AFrontIsABase_SoItHasNoRenderSource(SceneImageReferenceBodyState state)
    {
        // It is generated or uploaded. A chain that named a source for it would let the UI offer "render" on the one
        // view that cannot be rendered.
        Assert.Null(BodyViewDerivation.RenderBaseFor(Key(state, SceneImageReferenceBodyView.Front)));
    }

    /// <summary>
    /// The two rules answer different questions — what a render is CONDITIONED on versus what an edit TURNS — and they
    /// differ in exactly three cells, each for a reason:
    ///
    /// <list type="bullet">
    /// <item>a CLOTHED profile: the render turns from the front (measured working, 2026-09-23) while the edit rotates
    /// the same-side three-quarter;</item>
    /// <item>the UNCLOTHED front: the render has no source — it is the state's base and is produced as an EDIT of the
    /// clothed front, which is why <c>RenderBaseFor</c> answers null there and <c>EditSourceFor</c> does not.</item>
    /// </list>
    ///
    /// Pinned, because an unexplained difference is exactly what let the render gate and the edit chain disagree about
    /// a profile in the first place.
    /// </summary>
    [Fact]
    public void TheRenderBaseAndTheEditSource_DifferInExactlyOneCell()
    {
        var states = new[] { SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyState.Unclothed };
        var views = Enum.GetValues<SceneImageReferenceBodyView>();
        var differences = new List<string>();

        foreach (var state in states)
        {
            foreach (var view in views)
            {
                var key = Key(state, view);
                if (BodyViewDerivation.RenderBaseFor(key) != BodyViewDerivation.EditSourceFor(key))
                {
                    differences.Add(key.Describe());
                }
            }
        }

        Assert.Equal(
            ["Clothed:ProfileLeft", "Clothed:ProfileRight", "Unclothed:Front"],
            differences.OrderBy(text => text, StringComparer.Ordinal));
    }

    /// <summary>
    /// An unclothed FRONT is an edit of the clothed front — the same body with the clothes removed — and never a
    /// render, so the two rules agree there and the clothed front is the root of the whole chain.
    /// </summary>
    [Fact]
    public void TheClothedFrontIsTheRoot_AndTheUnclothedFrontIsEditedFromIt()
    {
        var clothedFront = Key(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front);

        Assert.Null(BodyViewDerivation.EditSourceFor(clothedFront));
        Assert.Equal(
            clothedFront,
            BodyViewDerivation.EditSourceFor(Key(SceneImageReferenceBodyState.Unclothed, SceneImageReferenceBodyView.Front)));
    }

    /// <summary>
    /// An extended view (a rotation with a position key) is produced as an edit of the front of its state, and has no
    /// canonical slot of its own — the chain must still answer for it rather than throwing.
    /// </summary>
    [Fact]
    public void AnExtendedView_ResolvesAgainstItsStatesFront()
    {
        var extended = CharacterIdentityBodyViewKey.Extended(
            SceneImageReferenceBodyState.Clothed, rotationDeg: 45, positionKey: "standing");

        Assert.Equal(
            Key(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front),
            BodyViewDerivation.RenderBaseFor(extended));
        Assert.Equal(
            Key(SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front),
            BodyViewDerivation.EditSourceFor(extended));
    }
}
