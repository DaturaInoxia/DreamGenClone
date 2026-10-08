namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The UI contract for the image-tag surface (B-140 D2/FR-6/FR-10): where tags are shown, where they are edited, and
/// where the reference-image search lives.
///
/// <para>
/// These are source contracts rather than rendered assertions, matching the other UI contract tests in this project:
/// what is pinned is the SHAPE the surfaces must keep — one tag component reused read-only and editable, the editor
/// reachable from the image's own card, and the search reaching an image service instead of filtering a list the page
/// already holds (which would silently search only what happened to be on screen).
/// </para>
/// </summary>
public sealed class ImageTagUiContractTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string TagComponentSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Assets", "ImageTags.razor"));
    private static readonly string ManagerSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Pages", "AssetStudio.razor"));
    private static readonly string DetailSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Pages", "AssetStudioView.razor"));
    private static readonly string ReviewSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Pages", "AssetReview.razor"));

    [Fact]
    public void TagComponent_OffersEditingOnlyWhenAskedAndAlwaysThroughTheCatalog()
    {
        Assert.Contains("@inject ISceneAssetImageTagService TagService", TagComponentSource, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public bool Editable", TagComponentSource, StringComparison.Ordinal);
        Assert.Contains("ImageTagCatalog.Parse(Image.TagsJson)", TagComponentSource, StringComparison.Ordinal);

        // The axis is a PICK LIST from the catalog's own prefixes: a hand-typed prefix would be stored (or refused) as a
        // surprise, and the operator would have no way to know which axes exist.
        Assert.Contains("foreach (var prefix in ImageTagCatalog.Prefixes)", TagComponentSource, StringComparison.Ordinal);

        // The exact tag about to be written is shown, so "what will this be called" is answered before the click rather
        // than after a search fails to find it.
        Assert.Contains("@_preview", TagComponentSource, StringComparison.Ordinal);

        // A refusal from the service is surfaced, never swallowed: a tag edit that quietly did nothing is the failure
        // nobody notices.
        Assert.Contains("@_error", TagComponentSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AssetDetailPage_ShowsTheTagEditorOnCompletedImagesOnly()
    {
        // The card is the shared ImageCard; the detail page renders it from the one model helper.
        Assert.Contains("<ImageCard Model=\"CardModel(image)\"", DetailSource, StringComparison.Ordinal);

        // The tag editor is host content handed to the card, and it sits inside the completed-image branch so a
        // pending or failed row never offers to tag a picture that does not exist.
        Assert.Contains("<ImageTags Image=\"image\" Editable=\"true\" TagsChanged=\"LoadAsync\" />",
            DetailSource, StringComparison.Ordinal);
        var tagEditor = DetailSource.IndexOf("<ImageTags Image=\"image\" Editable=\"true\"", StringComparison.Ordinal);
        var completionGuard = DetailSource.LastIndexOf(
            "image.Status == SceneAssetStatus.Complete", tagEditor, StringComparison.Ordinal);
        Assert.True(completionGuard >= 0,
            "The tag editor must sit inside the completed-image branch of the asset detail page.");
    }

    [Fact]
    public void AssetManager_SearchesTagsThroughTheServiceAndShowsMatchingChips()
    {
        Assert.Contains("@inject ISceneAssetImageTagService TagService", ManagerSource, StringComparison.Ordinal);
        Assert.Contains("await TagService.SearchImagesByTagAsync(_tagSearch)", ManagerSource, StringComparison.Ordinal);

        // The results show their OWN tags read-only, which is what makes a hit explicable ("why did this match?").
        Assert.Contains("<ImageTags Image=\"tagged\" />", ManagerSource, StringComparison.Ordinal);

        // The search matches a tag VALUE, and the page says so: an operator who typed "lora" and found nothing because
        // it is a prefix needs to know that, not to conclude the library is empty.
        Assert.Contains("Matches a tag's value", ManagerSource, StringComparison.Ordinal);

        // The reference scope is the SAME bar the reference pickers use — the production approval — rather than a guess
        // about which pictures "look like" references.
        Assert.Contains("_tagSearchReferencesOnly", ManagerSource, StringComparison.Ordinal);
        Assert.Contains("SceneAssetProductionApprovalStatus.Approved", ManagerSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// The review surface is where an operator decides what an image IS, so it both tags the image and hands the
    /// round-trip over in one click — through the asset page's own load, so there is one implementation of "restore what
    /// made this image" rather than two that can drift.
    /// </summary>
    [Fact]
    public void ReviewPage_TagsTheImageAndRoutesTheRoundTripToTheGeneratorsOwnLoad()
    {
        Assert.Contains("<ImageTags Image=\"_image\" Editable=\"true\" />", ReviewSource, StringComparison.Ordinal);
        Assert.Contains("href=\"/asset-studio/@AssetId?loadImageId=@ImageId\"", ReviewSource, StringComparison.Ordinal);

        // The consuming half of that link: the asset page reads the parameter and applies the load through the SAME
        // method the on-page button uses.
        Assert.Contains("[SupplyParameterFromQuery(Name = \"loadImageId\")]", DetailSource, StringComparison.Ordinal);
        Assert.Contains("ApplyRequestedDraftLoad();", DetailSource, StringComparison.Ordinal);
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
