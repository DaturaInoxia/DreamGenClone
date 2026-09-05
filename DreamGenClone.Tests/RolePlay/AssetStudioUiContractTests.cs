namespace DreamGenClone.Tests.RolePlay;

public sealed class AssetStudioUiContractTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string ManagerSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Pages", "AssetStudio.razor"));
    private static readonly string DetailSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Pages", "AssetStudioView.razor"));
    private static readonly string CreateSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Pages", "AssetCreate.razor"));
    private static readonly string EditSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Pages", "AssetEdit.razor"));
    private static readonly string ReviewSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Pages", "AssetReview.razor"));
    private static readonly string EditComponentSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Assets", "ImageEditWorkbench.razor"));
    [Fact]
    public void Manager_ListsAssetsAndLinksDedicatedManagementWorkflows()
    {
        Assert.Contains("Asset Library", ManagerSource, StringComparison.Ordinal);
        Assert.Contains("@bind=\"_assetSearch\"", ManagerSource, StringComparison.Ordinal);
        Assert.Contains("@bind=\"_assetTypeFilter\"", ManagerSource, StringComparison.Ordinal);
        Assert.Contains("@bind=\"_assetApprovalFilter\"", ManagerSource, StringComparison.Ordinal);
        Assert.Contains("@bind=\"_assetCharacterFilter\"", ManagerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateFromPromptAsync", ManagerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateFromUploadAsync", ManagerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EnqueueProfilePackAsync", ManagerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ApproveForProductionAsync", ManagerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("InputFile", ManagerSource, StringComparison.Ordinal);
        Assert.Contains("href=\"/assets/create\"", ManagerSource, StringComparison.Ordinal);
        Assert.Contains("href=\"/characters/identity\"", ManagerSource, StringComparison.Ordinal);
        Assert.Contains("href=\"/asset-studio/lora-datasets/new\"", ManagerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Character Versions", ManagerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Operations_HaveDedicatedRoutesAndReusableComponents()
    {
        Assert.Contains("@page \"/assets/create\"", CreateSource, StringComparison.Ordinal);
        Assert.Contains("CreateAssetAsync(_name, _type!.Value)", CreateSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PromptAssetCreator", CreateSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AssetUploadCreator", CreateSource, StringComparison.Ordinal);
        Assert.Contains("<PromptAssetCreator AssetId=\"@_asset.Id\"", DetailSource, StringComparison.Ordinal);
        Assert.Contains("<AssetUploadCreator AssetId=\"@_asset.Id\"", DetailSource, StringComparison.Ordinal);
        Assert.Contains("@page \"/assets/{AssetId}/images/{ImageId}/edit\"", EditSource, StringComparison.Ordinal);
        Assert.Contains("<ImageEditWorkbench Source=\"_image\" />", EditSource, StringComparison.Ordinal);
        Assert.Contains("@page \"/assets/{AssetId}/images/{ImageId}/review\"", ReviewSource, StringComparison.Ordinal);
        Assert.Contains("<ProductionApprovalForm Image=\"_image\" />", ReviewSource, StringComparison.Ordinal);
        Assert.Contains("/assets/@_asset.Id/images/@image.Id/edit", DetailSource, StringComparison.Ordinal);
        Assert.Contains("/assets/@_asset.Id/images/@image.Id/review", DetailSource, StringComparison.Ordinal);
        Assert.Contains("await InvokeAsync(async () =>", DetailSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EnqueueEditAsync", DetailSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ApproveForProductionAsync", DetailSource, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(_outputCount, 1, 8)", EditComponentSource, StringComparison.Ordinal);
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