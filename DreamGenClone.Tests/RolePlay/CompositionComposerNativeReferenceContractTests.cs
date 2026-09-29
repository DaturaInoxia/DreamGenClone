namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The composition page must ask for the render the operator actually set up (2026-09-28).
///
/// A native-reference create arrives with NO face at all unless the caller names the packs: the render path binds
/// exactly the packs it is given (<c>SceneImageService.ResolveNativeReferenceIdentityBindingsAsync</c>). So the mode
/// is derived from the step's own BINDINGS - a face bound from an identity pack IS the request - rather than from a
/// second control the operator would have to find and set, which is how the native path stayed unreachable from the
/// studio while the engine behind it was finished.
///
/// 'Identity on create' is a DIFFERENT mechanism (a configured IP-Adapter/PuLID graph) on a different model, so it
/// keeps its own control and still wins when the operator turned it on.
/// </summary>
public sealed class CompositionComposerNativeReferenceContractTests
{
    private static string Page => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Pages", "CompositionComposer.razor"));

    /// <summary>The bindings decide the mode, and the packs they name travel as the render's identity packs.</summary>
    [Fact]
    public void TheNativeReferenceModeIsDerivedFromTheBoundPacks()
    {
        Assert.Contains("var nativePacks = _referenceBindings", Page, StringComparison.Ordinal);
        // A binding qualifies on what it RESOLVED to, not on what it claims: no pack id or no asset id cannot be rendered.
        Assert.Contains("binding.IdentityPackId", Page, StringComparison.Ordinal);
        Assert.Contains("binding.ReferenceAssetId", Page, StringComparison.Ordinal);
        Assert.Contains("SceneImageRenderMode.NativeReference", Page, StringComparison.Ordinal);
        Assert.Contains("RenderMode = renderMode", Page, StringComparison.Ordinal);
        Assert.Contains("IdentityPacks = identityPacks ?? nativePacks", Page, StringComparison.Ordinal);
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
