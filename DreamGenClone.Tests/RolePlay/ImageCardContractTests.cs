namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The one-image-card contract: every list of asset images must render through the shared
/// <c>ImageCard</c> rather than a private copy, and the accept / shortlist / reject verdicts live in that card so a
/// decision taken on any surface is the same fact as one taken in the review deck.
/// </summary>
public sealed class ImageCardContractTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string CardSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Shared", "ImageCard.razor"));
    private static readonly string AssetStudioViewSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Pages", "AssetStudioView.razor"));
    private static readonly string GallerySource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Pages", "SceneImageGallery.razor"));
    private static readonly string BodyViewsPanelSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor"));
    private static readonly string SceneImageStudioSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Pages", "SceneImageStudio.razor"));

    [Fact]
    public void Card_RendersTheReviewDecksThreeVerdictsThroughOneCallbackEach()
    {
        // Each verdict is a host-wired callback, so the card never invents a decision enum for its host.
        Assert.Contains("[Parameter] public EventCallback AcceptRequested { get; set; }", CardSource, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public EventCallback ShortlistRequested { get; set; }", CardSource, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public EventCallback RejectRequested { get; set; }", CardSource, StringComparison.Ordinal);

        Assert.Contains("AcceptRequested.InvokeAsync()", CardSource, StringComparison.Ordinal);
        Assert.Contains("ShortlistRequested.InvokeAsync()", CardSource, StringComparison.Ordinal);
        Assert.Contains("RejectRequested.InvokeAsync()", CardSource, StringComparison.Ordinal);

        // A read-only host (no callback supplied) renders no verdict buttons at all, and a host can hide them.
        Assert.Contains("@if (ShowVerdicts && AcceptRequested.HasDelegate)", CardSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Card_IsTheSingleCardAssetStudioRenders()
    {
        // The /asset-studio list is the reference surface: it renders the shared card and opts into all three verdicts.
        Assert.Contains("<ImageCard Model=\"CardModel(image)\"", AssetStudioViewSource, StringComparison.Ordinal);
        Assert.Contains("AcceptRequested=\"() => DecideImageAsync(image, SceneAssetCandidateDecision.Accepted)\"",
            AssetStudioViewSource, StringComparison.Ordinal);
        Assert.Contains("ShortlistRequested=\"() => DecideImageAsync(image, SceneAssetCandidateDecision.Shortlisted)\"",
            AssetStudioViewSource, StringComparison.Ordinal);
        Assert.Contains("RejectRequested=\"() => DecideImageAsync(image, SceneAssetCandidateDecision.Rejected)\"",
            AssetStudioViewSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Card_ShowsThePictureAndItsStatusAndDecisionBadges()
    {
        Assert.Contains("src=\"/scene-images/@Model.FileRelativePath\"", CardSource, StringComparison.Ordinal);
        Assert.Contains("@Model.StatusText", CardSource, StringComparison.Ordinal);
        Assert.Contains("@Model.DecisionBadgeText", CardSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Gallery_RendersTheSameCardThroughItsOwnModel()
    {
        // The role-play gallery is a different entity (SceneImageRecord) but still renders the ONE card, projecting
        // through its own model helper rather than a private copy.
        Assert.Contains("<ImageCard Model=\"CardModel(img)\"", GallerySource, StringComparison.Ordinal);

        // Its verdicts are the gallery entity's own lifecycle: shortlist toggles, reject is one-way, and there is no
        // "accept" because Active is the implicit keep state — exactly like the review deck for the same entity.
        Assert.Contains("ShortlistRequested=\"() => ShortlistAsync(img)\"", GallerySource, StringComparison.Ordinal);
        Assert.Contains("RejectRequested=\"() => SetDispositionAsync(img, SceneImageAttemptDisposition.Rejected)\"",
            GallerySource, StringComparison.Ordinal);
        Assert.DoesNotContain("AcceptRequested=", GallerySource, StringComparison.Ordinal);

        // Legacy images (no production group) cannot take a disposition, so the verdicts are hidden for them.
        Assert.Contains("ShowVerdicts=\"@(img.ProductionGroupId is { Length: > 0 })\"", GallerySource, StringComparison.Ordinal);
    }

    [Fact]
    public void BodyCandidates_RenderTheSameCard()
    {
        // The characters body-view candidate deck is the shared card with the asset studio's full feature set: name,
        // tags, the three verdicts, edit/review/download/video, and prompt load — carried through the card's slots.
        Assert.Contains("<ImageCard Model=\"CandidateCardModel(candidate)\"", BodyViewsPanelSource, StringComparison.Ordinal);
        Assert.Contains("ShowName=\"true\"", BodyViewsPanelSource, StringComparison.Ordinal);
        Assert.Contains("NameChanged=\"name => RenameCandidateAsync(candidate, name)\"", BodyViewsPanelSource, StringComparison.Ordinal);
        Assert.Contains("<ImageTags Image=\"candidate\" Editable=\"true\"", BodyViewsPanelSource, StringComparison.Ordinal);
        Assert.Contains("AcceptRequested=\"() => AcceptCandidateAsync(key, candidate.Id)\"", BodyViewsPanelSource, StringComparison.Ordinal);
        Assert.Contains("ShortlistRequested=\"() => DecideCandidateAsync(key, candidate.Id, SceneAssetCandidateDecision.Shortlisted)\"",
            BodyViewsPanelSource, StringComparison.Ordinal);
        Assert.Contains("RejectRequested=\"() => DecideCandidateAsync(key, candidate.Id, SceneAssetCandidateDecision.Rejected)\"",
            BodyViewsPanelSource, StringComparison.Ordinal);
        Assert.Contains("EditRequested=\"() => OpenCandidateEdit(candidate)\"", BodyViewsPanelSource, StringComparison.Ordinal);
        Assert.Contains("ReviewRequested=\"() => OpenCandidateReview(candidate)\"", BodyViewsPanelSource, StringComparison.Ordinal);
        Assert.Contains("DownloadRequested=\"() => OpenCandidateDownload(candidate)\"", BodyViewsPanelSource, StringComparison.Ordinal);
        Assert.Contains("VideoRequested=\"() => OpenCandidateVideo(candidate)\"", BodyViewsPanelSource, StringComparison.Ordinal);
        Assert.Contains("LoadIntoGeneratorRequested=\"() => LoadCandidatePromptAsync(key, candidate)\"", BodyViewsPanelSource, StringComparison.Ordinal);
        Assert.Contains("AcceptTitle=\"Make this image this view's result and accept the view", BodyViewsPanelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void StudioAdditionalImages_RenderTheSameCard()
    {
        // The role-play studio's additional-images list is the shared card, with continue/regenerate through the footer
        // slot and the viewer reached through the card's view callback.
        Assert.Contains("<ImageCard Model=\"CardModel(img)\"", SceneImageStudioSource, StringComparison.Ordinal);
        Assert.Contains("ViewRequested=\"() => OpenViewer(img)\"", SceneImageStudioSource, StringComparison.Ordinal);
        Assert.Contains("EditRequested=\"() => OpenImageEditor(img)\"", SceneImageStudioSource, StringComparison.Ordinal);
        Assert.Contains("ContinueFromImageAsync(img)", SceneImageStudioSource, StringComparison.Ordinal);
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
