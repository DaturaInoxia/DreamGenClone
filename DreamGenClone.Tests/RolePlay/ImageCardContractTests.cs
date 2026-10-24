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
    private static readonly string AppSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "App.razor"));
    private static readonly string ZoomScriptSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "wwwroot", "js", "image-zoom.js"));
    private static readonly string ReviewDeckSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Pages", "ReviewDeck.razor"));
    private static readonly string RailComponentSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Shared", "ImageMetadataRail.razor"));
    private static readonly string FilterComponentSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Shared", "ImageCardStatusFilter.razor"));
    private static readonly string StatusHelperSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Shared", "ImageCardStatus.cs"));
    private static readonly string InspectorSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Shared", "ImageInspector.razor"));

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
        Assert.Contains("<ImageTags ImageId=\"candidate.Id\" Target=\"ImageTagTarget.AssetLibrary\"",
            BodyViewsPanelSource, StringComparison.Ordinal);
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

        // Undecide is an icon-only "?" so the footer stays a row of buttons; the meaning lives in the tooltip.
        Assert.Contains("title=\"Return this candidate to Undecided\"><i class=\"bi bi-question-lg\"></i></button>",
            BodyViewsPanelSource, StringComparison.Ordinal);
        Assert.DoesNotContain(">Undecide</button>", BodyViewsPanelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void StudioAdditionalImages_RenderTheSameCard()
    {
        // The role-play studio's additional-images list is the shared card, with continue/regenerate through the footer
        // slot and the viewer reached through the card's view callback.
        Assert.Contains("<ImageCard Model=\"CardModel(img)\"", SceneImageStudioSource, StringComparison.Ordinal);
        Assert.Contains("EditRequested=\"() => OpenImageEditor(img)\"", SceneImageStudioSource, StringComparison.Ordinal);
        Assert.Contains("ContinueFromImageAsync(img)", SceneImageStudioSource, StringComparison.Ordinal);

        // The card's own picture click opens its zoom inspector; the studio no longer hands the click to a second,
        // zoomless overlay.
        Assert.DoesNotContain("ViewRequested=\"() => OpenViewer(img)\"", SceneImageStudioSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Card_OwnsTheFullSizeViewerEveryHostGetsForFree()
    {
        // The inspector is ONE shared component, so the card and the review deck open the identical popup rather than
        // two that look alike until one of them is changed.
        Assert.Contains("title=\"View larger\"", CardSource, StringComparison.Ordinal);
        Assert.Contains("bi bi-arrows-fullscreen", CardSource, StringComparison.Ordinal);
        Assert.Contains("<ImageInspector Open=\"_viewerOpen\"", CardSource, StringComparison.Ordinal);
        Assert.Contains("modal fade show d-block", InspectorSource, StringComparison.Ordinal);

        // The picture itself opens the same viewer, and a host that wants different behaviour still can.
        Assert.Contains("@onclick=\"OnPictureClickAsync\"", CardSource, StringComparison.Ordinal);
        Assert.Contains("if (ViewRequested.HasDelegate)", CardSource, StringComparison.Ordinal);

        // The review deck's big image opens that same inspector — its old bespoke modal is gone.
        Assert.Contains("<ImageInspector Open=\"_viewerOpen\"", ReviewDeckSource, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"modal fade show d-block\"", ReviewDeckSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// The review deck's strip is the shared card too, selecting rather than opening: the deck is choosing which
    /// candidate to look at, so the picture and the select button pick the one shown above.
    /// </summary>
    [Fact]
    public void ReviewDeck_UsesTheSharedCardAndSelectsThroughIt()
    {
        Assert.Contains("<ImageCard Model=\"CardModel(item)\"", ReviewDeckSource, StringComparison.Ordinal);
        Assert.Contains("SelectRequested=\"() => SelectCandidateAsync(item.Id)\"", ReviewDeckSource, StringComparison.Ordinal);
        Assert.Contains("IsSelected = item.Id == Current?.Id", ReviewDeckSource, StringComparison.Ordinal);

        // Rejecting is one click everywhere: a modal confirmation on an action that is itself reversible is friction,
        // and the deck's own verdict buttons already say what they do.
        Assert.DoesNotContain("JS.InvokeAsync<bool>(\"confirm\"", ReviewDeckSource, StringComparison.Ordinal);

        // The toolbar is icon-only, the way the card's footer is: no action is a word in a button.
        Assert.DoesNotContain(">Previous</button>", ReviewDeckSource, StringComparison.Ordinal);
        Assert.DoesNotContain(">Next</button>", ReviewDeckSource, StringComparison.Ordinal);
        Assert.DoesNotContain(">Compare</button>", ReviewDeckSource, StringComparison.Ordinal);
        Assert.DoesNotContain(">Shortlist</button>", ReviewDeckSource, StringComparison.Ordinal);
        Assert.DoesNotContain(">Reject</button>", ReviewDeckSource, StringComparison.Ordinal);
        Assert.DoesNotContain(">Accept</button>", ReviewDeckSource, StringComparison.Ordinal);
        Assert.DoesNotContain(">Revoke</button>", ReviewDeckSource, StringComparison.Ordinal);
        Assert.DoesNotContain("View larger", ReviewDeckSource, StringComparison.Ordinal);

        // The rail stretches to the image's row and scrolls only when its own content is taller. The rail's own markup
        // — including the audit footer — lives in ONE shared component now, so the deck keeps the wrapper and the
        // component keeps the field list; the contract is asserted against the file that owns each half.
        Assert.Contains("review-deck-body", ReviewDeckSource, StringComparison.Ordinal);
        Assert.Contains(".review-deck-rail {", ReviewDeckSource, StringComparison.Ordinal);
        Assert.Contains("<div class=\"review-deck-rail\">", ReviewDeckSource, StringComparison.Ordinal);
        Assert.Contains("review-deck-rail-footer", RailComponentSource, StringComparison.Ordinal);

        // ONE rail for every route: the deck renders the shared component once, rather than a branch per route.
        Assert.Contains("<ImageMetadataRail Item=\"Current\"", ReviewDeckSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// The status filter is one shared component and one shared vocabulary, applied in the heading of every list that
    /// renders the card — otherwise "filter by status" would mean something different on each page.
    /// </summary>
    [Fact]
    public void EveryCardList_OffersTheSharedStatusFilterInItsHeading()
    {
        Assert.Contains("ImageCardStatusFilter", AssetStudioViewSource, StringComparison.Ordinal);
        Assert.Contains("ImageCardStatusFilter", ReviewDeckSource, StringComparison.Ordinal);
        Assert.Contains("ImageCardStatusFilter", GallerySource, StringComparison.Ordinal);
        Assert.Contains("ImageCardStatusFilter", SceneImageStudioSource, StringComparison.Ordinal);
        Assert.Contains("ImageCardStatusFilter", BodyViewsPanelSource, StringComparison.Ordinal);

        // Each list derives its options from the cards it actually holds, through the one builder.
        Assert.Contains("ImageCardStatus.Build(", AssetStudioViewSource, StringComparison.Ordinal);
        Assert.Contains("ImageCardStatus.Build(", ReviewDeckSource, StringComparison.Ordinal);

        // And the shared filter component only ever offers a view that has something in it.
        Assert.Contains("if (Options.Count > 1)", FilterComponentSource, StringComparison.Ordinal);
        Assert.Contains("new(null, \"All\", materialised.Count)", StatusHelperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Gallery_UsesTheCardsViewerRatherThanItsOwnOverlay()
    {
        Assert.DoesNotContain("ViewRequested=", GallerySource, StringComparison.Ordinal);
        Assert.DoesNotContain("scene-image-viewer-overlay", GallerySource, StringComparison.Ordinal);
    }

    [Fact]
    public void Card_DelegatesPanAndZoomToTheSharedInspectorScript()
    {
        // The stage, its controls and the readout are the shared inspector's own markup; the maths lives in one script,
        // so there is no per-host zoom implementation to drift.
        Assert.Contains("class=\"image-inspector-stage\"", InspectorSource, StringComparison.Ordinal);
        Assert.Contains("data-zoom-image", InspectorSource, StringComparison.Ordinal);
        Assert.Contains("data-zoom-readout", InspectorSource, StringComparison.Ordinal);
        Assert.Contains("data-zoom-action=\"in\"", InspectorSource, StringComparison.Ordinal);
        Assert.Contains("data-zoom-action=\"out\"", InspectorSource, StringComparison.Ordinal);
        Assert.Contains("data-zoom-action=\"fit\"", InspectorSource, StringComparison.Ordinal);
        Assert.Contains("data-zoom-action=\"actual\"", InspectorSource, StringComparison.Ordinal);

        Assert.Contains("JS.InvokeVoidAsync(\"imageZoom.attach\", _viewport, _selfReference)", InspectorSource, StringComparison.Ordinal);
        Assert.Contains("JS.InvokeVoidAsync(\"imageZoom.detach\", _viewport)", InspectorSource, StringComparison.Ordinal);

        // The script is served with the app, alongside the other small modules.
        Assert.Contains("js/image-zoom.js", AppSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectorScript_KeepsInspectionHonestAndNonDestructive()
    {
        // Natural pixels, scale-transformed: 100% is one image pixel per screen pixel, which is the whole point of
        // inspecting rather than looking at a thumbnail.
        Assert.Contains("image.style.width = image.naturalWidth + 'px';", ZoomScriptSource, StringComparison.Ordinal);
        Assert.Contains("transformOrigin = '0 0'", ZoomScriptSource, StringComparison.Ordinal);

        // Zoom is anchored at the cursor, so the detail under the pointer stays under the pointer.
        Assert.Contains("zoomTo: function (viewport, scale, anchorX, anchorY)", ZoomScriptSource, StringComparison.Ordinal);
        Assert.Contains("event.preventDefault();", ZoomScriptSource, StringComparison.Ordinal);

        // Ctrl/Cmd/Alt belong to the browser: inspecting an image must not block the browser's own zoom.
        Assert.Contains("if (event.ctrlKey || event.metaKey || event.altKey)", ZoomScriptSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// The action row sits DIRECTLY under the picture, above the badges, the id and the host's body content.
    ///
    /// <para>
    /// The buttons are what the operator reaches for, so they belong against the image they act on rather than below
    /// a paragraph of metadata (operator decision 2026-10-12). Asserted by DOCUMENT ORDER: the footer and the body are
    /// otherwise indistinguishable, and only their sequence says which is under the picture.
    /// </para>
    /// </summary>
    [Fact]
    public void Card_PutsItsActionRowDirectlyUnderThePicture()
    {
        var picture = CardSource.IndexOf("ratio ratio-1x1", StringComparison.Ordinal);
        var actions = CardSource.IndexOf("card-footer", StringComparison.Ordinal);
        var body = CardSource.IndexOf("card-body py-2 small", StringComparison.Ordinal);

        Assert.True(picture >= 0, "the card no longer renders a picture");
        Assert.True(actions >= 0, "the card no longer renders an action row");
        Assert.True(body >= 0, "the card no longer renders its badge body");

        Assert.True(picture < actions, "the action row must follow the picture");
        Assert.True(actions < body, "the action row must come BEFORE the badges, directly under the picture");
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
