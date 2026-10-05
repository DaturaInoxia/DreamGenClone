namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The composition page must ask for the render the operator actually set up (2026-09-28), and it must send the pack
/// reference through exactly ONE route (2026-10-02).
///
/// A native-reference create arrives with NO face at all unless the caller names the references: the render path binds
/// exactly what it is given. So the mode is derived from the step's own BINDINGS - a face bound from an identity pack IS
/// the request - rather than from a second control the operator would have to find and set, which is how the native
/// path stayed unreachable from the studio while the engine behind it was finished.
///
/// The reference itself then travels as that same binding, because a binding carries the EXACT view the operator picked
/// (their Profile Left face, their clothed build) while the pack channel resolves the pack's CANONICAL face. Two routes
/// for one face is two faces, so the channel is handed packs only for the graph route and the native route keeps the
/// bindings.
///
/// 'Identity on create' is a DIFFERENT mechanism (a configured IP-Adapter/PuLID graph) on a different model, so it
/// keeps its own control and still wins when the operator turned it on.
/// </summary>
public sealed class CompositionComposerNativeReferenceContractTests
{
    private static string Page => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Pages", "CompositionComposer.razor"));

    /// <summary>The bindings decide the mode, and the bindings are what the native render receives.</summary>
    [Fact]
    public void TheNativeReferenceModeIsDerivedFromTheBoundPacks()
    {
        Assert.Contains("var nativePacks = _referenceBindings", Page, StringComparison.Ordinal);
        // A binding qualifies on what it RESOLVED to, not on what it claims: no pack id or no asset id cannot be rendered.
        Assert.Contains("binding.IdentityPackId", Page, StringComparison.Ordinal);
        Assert.Contains("binding.ReferenceAssetId", Page, StringComparison.Ordinal);
        Assert.Contains("SceneImageRenderMode.NativeReference", Page, StringComparison.Ordinal);
        Assert.Contains("RenderMode = renderMode", Page, StringComparison.Ordinal);
        Assert.Contains("ReferenceApplications = _referenceBindings", Page, StringComparison.Ordinal);
        // The channel carries the packs for the GRAPH route only. Handing it the packs the bindings already name would
        // send the pack's canonical face beside the bound one - one character, two faces, two mechanisms.
        Assert.Contains("IdentityPacks = identityPacks,", Page, StringComparison.Ordinal);
        Assert.DoesNotContain("IdentityPacks = identityPacks ?? nativePacks", Page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two mechanisms are refused together rather than quietly stacked: a frame that applies identity through
    /// 'Identity on create' AND binds a pack face would condition on two faces of the same character.
    /// </summary>
    [Fact]
    public void TheTwoIdentityMechanismsAreRefusedTogether()
    {
        Assert.Contains("two identity mechanisms for one frame", Page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The bindings must reach the PROMPT GENERATOR too, not only the render. A prompt drafted with no bindings
    /// pre-describes everything the images are supposed to supply — the room, its lighting, the person — and the
    /// appended role clause then has to argue with a body that is longer, earlier and more specific than it. Measured
    /// 2026-10-03 on a prompt drafted before its references were bound: mean brightness 43.5 against its reference's
    /// 72.0, and outer-ring L1 2.094 from its own bound room — as far as two different views of that shed are from
    /// each other (2.141). The generator already derives removals from these bindings and states them to the model as
    /// a USER REMOVALS notice; the page simply never handed them over.
    ///
    /// <para>
    /// Asserted by COUNT as well, because <c>ReferenceApplications = _referenceBindings</c> legitimately appears in
    /// both the prompt enqueue and the render enqueue, and this guard is about the prompt one existing at all.
    /// </para>
    /// </summary>
    [Fact]
    public void TheBoundReferencesAlsoReachThePromptGenerator()
    {
        var occurrences = Page.Split("ReferenceApplications = _referenceBindings").Length - 1;

        Assert.True(
            occurrences >= 2,
            $"The composition page must hand its bindings to the prompt generator as well as the render, but "
            + $"'ReferenceApplications = _referenceBindings' appears {occurrences} time(s).");
    }

    /// <summary>
    /// The flag can no longer pick the mode on its own: with it off the page used to hardcode <c>PromptOnly</c>, which
    /// is what threw the bound face away.
    /// </summary>
    [Fact]
    public void TheModeIsNoLongerTheIdentityFlagAlone()
    {
        Assert.DoesNotContain(
            "RenderMode = _applyIdentityOnCreate ? SceneImageRenderMode.IdentityControlled : SceneImageRenderMode.PromptOnly",
            Page,
            StringComparison.Ordinal);
        // And the graph route is still reachable - it is the first branch, not a leftover.
        Assert.Contains("? SceneImageRenderMode.IdentityControlled", Page, StringComparison.Ordinal);
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
}
