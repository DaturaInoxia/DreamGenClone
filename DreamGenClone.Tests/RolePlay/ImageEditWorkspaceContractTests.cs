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
        // Identity is a store capability the one workspace offers wherever the store implements it, so BOTH
        // stores answer it and both edit surfaces show the tab. A store that cannot bind identity packs would
        // say so with SupportsIdentity => false and the tab would simply not exist for that subject.
        Assert.Contains("SupportsIdentity => true", SceneAdapterSource, StringComparison.Ordinal);
        Assert.Contains("SupportsIdentity => true", AssetAdapterSource, StringComparison.Ordinal);
        Assert.Contains("IImageIdentityEditService", AssetAdapterSource, StringComparison.Ordinal);
        Assert.Contains("ShowIdentity=\"true\"", SceneEditorSource, StringComparison.Ordinal);
        Assert.Contains("ShowIdentity=\"true\"", AssetEditorSource, StringComparison.Ordinal);

        // The character-identity build step is not an image-edit surface: it never requests the tab.
        Assert.Contains("ShowIdentity=\"false\"", CharacterStudioSource, StringComparison.Ordinal);
        Assert.Contains("IdentityAvailable", WorkspaceSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// The editor form decides the editor model, once, for every run it starts. The identity run must carry
    /// that same choice (a second resolution inside the pipeline is how a user's chosen model gets silently
    /// swapped for a configured default), and the roster must be asked about the whole subject, because the
    /// roster's source is store-specific and the form does not know which field names it.
    /// </summary>
    [Fact]
    public void TheFormDecidesTheModel_ForIdentityToo()
    {
        Assert.Contains("RunIdentityEditAsync(Subject, selections, _selectedEditorModelId!)", WorkspaceSource, StringComparison.Ordinal);
        Assert.Contains("LoadRosterAsync(Subject)", WorkspaceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadRosterAsync(Subject.SessionId", WorkspaceSource, StringComparison.Ordinal);

        // The tab refuses to run without a chosen model, exactly as the Edit tab's run does.
        Assert.Contains("string.IsNullOrWhiteSpace(_selectedEditorModelId)", WorkspaceSource, StringComparison.Ordinal);

        // And neither adapter resolves one of its own for the identity run.
        Assert.Contains("EditorModelId = (editorModelId ?? string.Empty).Trim()", SceneAdapterSource, StringComparison.Ordinal);
        Assert.Contains("EditorModelId = editorModelId", AssetAdapterSource, StringComparison.Ordinal);
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

    /// <summary>
    /// The asset adapter's first resolution of a result must respect the subject's candidate batch when it has
    /// one. Without it the resolver returned "the newest image derived from the subject image", and since the
    /// face-angle cards share the front container, each card adopted whatever the container produced last —
    /// left recording right's renders and vice versa (B-121 note 010). The tracked-result path is unaffected, so
    /// a run started in this session still reports its own image.
    /// </summary>
    [Fact]
    public void AssetResultResolution_MustStayInTheSubjectsCandidateBatch()
    {
        Assert.Contains("ResolveResultAsync", AssetAdapterSource, StringComparison.Ordinal);
        Assert.Contains("subject.CandidateBatchId", AssetAdapterSource, StringComparison.Ordinal);
        Assert.Contains("image.CandidateBatchId", AssetAdapterSource, StringComparison.Ordinal);

        // The scene adapter resolves by session instead — that one was already correct.
        Assert.Contains("image.EditSessionId, sessionId", SceneAdapterSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// A FAILED source description must not disable the editor forever. The workspace reported "work in flight" for
    /// as long as no description text arrived, so when the description job failed (2026-09-24: the provider served a
    /// different model id) every control stayed greyed out with nothing on screen, and the poll loop never stopped
    /// (debug/071). The description is context for the compiler, not a prerequisite for preparing an edit.
    /// </summary>
    [Fact]
    public void AFailedSourceDescription_IsReported_InsteadOfWaitingForever()
    {
        Assert.Contains("GetDescriptionOutcomeAsync(_session.Id)", WorkspaceSource, StringComparison.Ordinal);
        Assert.Contains("The source description could not be produced", WorkspaceSource, StringComparison.Ordinal);

        // Both adapters answer it from the description job's deterministic row, never from a heuristic.
        Assert.Contains("GetDescriptionOutcomeAsync", AssetAdapterSource, StringComparison.Ordinal);
        Assert.Contains("BackgroundJobTypes.SceneAssetImageEditDescription", AssetAdapterSource, StringComparison.Ordinal);
        Assert.Contains("GetDescriptionOutcomeAsync", SceneAdapterSource, StringComparison.Ordinal);
        Assert.Contains("BackgroundJobTypes.SceneImageEditDescription", SceneAdapterSource, StringComparison.Ordinal);
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
