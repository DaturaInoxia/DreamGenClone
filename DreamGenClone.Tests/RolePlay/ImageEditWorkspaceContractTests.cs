using DreamGenClone.Web.Application.RolePlay.Editing;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Contract for B-124's shared image create/edit primitive: one editor form, one edit path.
/// These tests exist so a fix or feature can never be added to "just one" edit screen again.
/// </summary>
public sealed class ImageEditWorkspaceContractTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static readonly string WorkspaceSource = Read("Components", "Editing", "ImageEditWorkspace.razor");
    private static readonly string SceneEditorSource = Read("Components", "Pages", "SceneImageEditor.razor");
    private static readonly string AssetEditorSource = Read("Components", "Pages", "AssetEdit.razor");
    private static readonly string CharacterStudioSource = Read("Components", "Pages", "CharacterStudio.razor");
    private static readonly string SceneAdapterSource = Read("Application", "RolePlay", "Editing", "SceneImageEditWorkspaceService.cs");
    private static readonly string AssetAdapterSource = Read("Application", "RolePlay", "Editing", "SceneAssetImageEditWorkspaceService.cs");

    [Fact]
    public void EveryEditSurface_RendersTheOneSharedWorkspace()
    {
        Assert.Contains("<ImageEditWorkspace Subject=", SceneEditorSource, StringComparison.Ordinal);
        Assert.Contains("<ImageEditWorkspace Subject=", AssetEditorSource, StringComparison.Ordinal);
        Assert.Contains("<ImageEditWorkspace Subject=", CharacterStudioSource, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDuplicateAssetEditorForm_NoLongerExists()
    {
        Assert.False(
            File.Exists(Path.Combine(Root, "DreamGenClone.Web", "Components", "Assets", "ImageEditWorkbench.razor")),
            "ImageEditWorkbench was the second edit form and must not come back.");
    }

    [Fact]
    public void Hosts_DoNotOwnTheirOwnEditPipeline()
    {
        // A host may only route; compiling, running and revision bookkeeping live in the workspace.
        Assert.DoesNotContain("EditIterateWorkbench", SceneEditorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EditIterateWorkbench", AssetEditorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ImageEditCompilationService", SceneEditorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ImageEditCompilationService", AssetEditorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CompilationService", SceneEditorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CompilationService", AssetEditorSource, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheWorkspace_ComposesTheIterateWorkbench()
    {
        var offenders = Directory
            .EnumerateFiles(Path.Combine(Root, "DreamGenClone.Web", "Components"), "*.razor", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("EditIterateWorkbench", StringComparison.Ordinal))
            .Select(file => Path.GetFileName(file))
            .Where(name => !string.Equals(name, "ImageEditWorkspace.razor", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void BothStores_AreReachedThroughTheSameContract()
    {
        Assert.Contains("IImageEditWorkspaceService", SceneAdapterSource, StringComparison.Ordinal);
        Assert.Contains("IImageEditWorkspaceService", AssetAdapterSource, StringComparison.Ordinal);
        Assert.Contains("ImageEditSubjectKind.SceneImage", SceneAdapterSource, StringComparison.Ordinal);
        Assert.Contains("ImageEditSubjectKind.AssetImage", AssetAdapterSource, StringComparison.Ordinal);
    }

    [Fact]
    public void IdentityIsACapability_NotAFork()
    {
        // Identity is the studio's extra capability; the character-identity step must not request it.
        Assert.Contains("SupportsIdentity => true", SceneAdapterSource, StringComparison.Ordinal);
        Assert.Contains("SupportsIdentity => false", AssetAdapterSource, StringComparison.Ordinal);
        Assert.Contains("ShowIdentity=\"true\"", SceneEditorSource, StringComparison.Ordinal);
        Assert.Contains("ShowIdentity=\"false\"", CharacterStudioSource, StringComparison.Ordinal);
        Assert.Contains("IdentityAvailable", WorkspaceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void UnregisteredSubjectKind_FailsFast()
    {
        var resolver = new ImageEditWorkspaceServiceResolver([], []);

        var error = Assert.Throws<InvalidOperationException>(
            () => resolver.Resolve(ImageEditSubjectKind.AssetImage));

        Assert.Contains("AssetImage", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Crop is an operation of the same workspace, not a separate screen: every edit surface must keep
    /// offering it, with a preview the user can actually see (a full-frame window is still drawn).
    /// </summary>
    [Fact]
    public void CropIsAnOperationOfTheOneWorkspace_WithAVisiblePreview()
    {
        Assert.Contains("RunCropAsync", WorkspaceSource, StringComparison.Ordinal);
        Assert.Contains("scene-crop-preview-window", WorkspaceSource, StringComparison.Ordinal);
        Assert.Contains("scene-crop-preview-corner", WorkspaceSource, StringComparison.Ordinal);
        Assert.Contains("CropAspectPresets", WorkspaceSource, StringComparison.Ordinal);
        Assert.Contains("Measure head", WorkspaceSource, StringComparison.Ordinal);
        Assert.Contains("CropRemovesNothing", WorkspaceSource, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments)
        => File.ReadAllText(Path.Combine([Root, "DreamGenClone.Web", .. segments]));

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "DreamGenClone.sln")))
                return current.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the DreamGenClone repository root.");
    }
}
