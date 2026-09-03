namespace DreamGenClone.Tests.RolePlay;

public sealed class AssetStudioUiContractTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string ManagerSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Pages", "AssetStudio.razor"));
    private static readonly string DetailSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Pages", "AssetStudioView.razor"));
    private static readonly string ModelResolutionSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Application", "ModelManager", "ModelResolutionService.cs"));

    [Fact]
    public void Manager_ExposesPrimaryAssetIdentityAndLoraCommands()
    {
        Assert.Contains("> Create Asset", ManagerSource, StringComparison.Ordinal);
        Assert.Contains("> Create Identity Pack", ManagerSource, StringComparison.Ordinal);
        Assert.Contains("> Create LoRA", ManagerSource, StringComparison.Ordinal);
        Assert.Contains("AssetCreateMode.Asset", ManagerSource, StringComparison.Ordinal);
        Assert.Contains("AssetCreateMode.IdentityPack", ManagerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AssetCreation_RequiresTypeAndPinsVisibleModelAndOutputs()
    {
        Assert.Contains("@bind=\"_promptType\"", ManagerSource, StringComparison.Ordinal);
        Assert.Contains("@bind=\"_uploadType\"", ManagerSource, StringComparison.Ordinal);
        Assert.Contains("@bind=\"_selectedGenerationModelId\"", ManagerSource, StringComparison.Ordinal);
        Assert.Contains("(Default)", ManagerSource, StringComparison.Ordinal);
        Assert.Contains("@bind=\"_generationOutputCount\"", ManagerSource, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(_generationOutputCount, 1, 8)", ManagerSource, StringComparison.Ordinal);
        Assert.Contains("_promptType.Value, _selectedGenerationModelId, _generationImageSize", ManagerSource, StringComparison.Ordinal);
        Assert.Contains(
            ".Where(item => string.IsNullOrWhiteSpace(item.ImageEditorDiffusionModel))",
            ModelResolutionSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void IdentityPack_PinsFrontAndEditorModels()
    {
        Assert.Contains("@bind=\"_packFrontModelId\"", ManagerSource, StringComparison.Ordinal);
        Assert.Contains("@bind=\"_packEditorModelId\"", ManagerSource, StringComparison.Ordinal);
        Assert.Contains("FrontModelId =", ManagerSource, StringComparison.Ordinal);
        Assert.Contains("EditorModelId =", ManagerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailEdit_PinsEditorSupportsOutputsAndUsesDispatcherPolling()
    {
        Assert.Contains("@bind=\"_selectedEditorModelId\"", DetailSource, StringComparison.Ordinal);
        Assert.Contains("@bind=\"_editOutputCount\"", DetailSource, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(_editOutputCount, 1, 8)", DetailSource, StringComparison.Ordinal);
        Assert.Contains("await InvokeAsync(async () =>", DetailSource, StringComparison.Ordinal);
        Assert.Contains("_editPrompt, _selectedEditorModelId", DetailSource, StringComparison.Ordinal);
    }

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