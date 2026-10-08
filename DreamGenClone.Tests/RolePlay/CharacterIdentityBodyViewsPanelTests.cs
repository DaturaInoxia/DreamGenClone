using DreamGenClone.Domain.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-122 E-2: the clothed and unclothed view grids. The slots are defined once in the domain, the grid renders them
/// from that definition, and every action in the panel belongs to ONE view — there is no batch acquisition to
/// "helpfully" run, because a body view is corrected one request at a time.
/// </summary>
public sealed class CharacterIdentityBodyViewsPanelTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    [Fact]
    public void TheCanonicalSlots_AreSixPerState_WithOneBaseEach()
    {
        // Six per state since 2026-09-24: the back view was added as the last angle (operator request). The assertion
        // below that EVERY enum value appears once per state is what makes that addition safe — a new view member with no
        // slot, or a slot with no member, fails here rather than silently dropping a pack slot.
        Assert.Equal(12, CharacterIdentityBodySlots.All.Count);
        Assert.Equal(6, CharacterIdentityBodySlots.For(SceneImageReferenceBodyState.Clothed).Count);
        Assert.Equal(6, CharacterIdentityBodySlots.For(SceneImageReferenceBodyState.Unclothed).Count);

        foreach (var state in new[] { SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyState.Unclothed })
        {
            var baseSlot = Assert.Single(CharacterIdentityBodySlots.For(state), slot => slot.IsBase);
            Assert.Equal(SceneImageReferenceBodyView.Front, baseSlot.View);
            // Every view is present exactly once per state: a missing one would silently drop a pack slot.
            Assert.Equal(
                Enum.GetValues<SceneImageReferenceBodyView>().OrderBy(view => view),
                CharacterIdentityBodySlots.For(state).Select(slot => slot.View).OrderBy(view => view));
        }

        Assert.Equal(
            CharacterIdentityBodySlots.All.Count,
            CharacterIdentityBodySlots.All.Select(slot => slot.Label).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AStateViewPairThatIsNotACanonicalSlot_IsRefusedNamingTheExtendedAlternative()
    {
        var error = Assert.Throws<InvalidOperationException>(() => CharacterIdentityBodySlots.Require(
            SceneImageReferenceBodyState.Clothed, (SceneImageReferenceBodyView)99));

        Assert.Contains("not one of the canonical body slots", error.Message, StringComparison.Ordinal);
        Assert.Contains("extended view", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExtendedView_IsLabelledByItsRotationAndPosition()
    {
        var view = new CharacterIdentityBodyView
        {
            State = SceneImageReferenceBodyState.Unclothed,
            RotationDeg = 45,
            PositionKey = "kneeling"
        };

        var label = CharacterIdentityBodySlots.LabelFor(view);

        Assert.Contains("45", label, StringComparison.Ordinal);
        Assert.Contains("kneeling", label, StringComparison.Ordinal);
    }

    /// <summary>
    /// The panel is a compare deck, not a single-result view (operator decision, 2026-09-22): every attempt the view
    /// has produced stays listed, and each can be decided without deleting anything. The card is the shared ImageCard
    /// (operator request: it must match the asset studio card), so a candidate's prompt is restored through the card's
    /// "load into generator" action rather than shown inline. A re-run must never make the previous result unreachable.
    /// </summary>
    [Fact]
    public void TheGridListsEveryCandidate_WithItsPromptAndModel()
    {
        var panel = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        // The history comes from the batch every generation joins, not from the view's single output pointer — that
        // pointer is what made a re-run look like the earlier images had been thrown away.
        Assert.Contains("image.CandidateBatchId", panel, StringComparison.Ordinal);
        Assert.Contains("CandidatesFor(key)", panel, StringComparison.Ordinal);
        Assert.Contains("key.BatchIdFor(BuildId)", panel, StringComparison.Ordinal);

        // Each candidate can be decided without deleting anything, and its prompt is restored into the view's prompt
        // box through the card's load-into-generator action — the asset-studio card's own round-trip.
        Assert.Contains("SetImageCandidateDecisionAsync", panel, StringComparison.Ordinal);
        Assert.Contains("LoadIntoGeneratorRequested=\"() => LoadCandidatePromptAsync(key, candidate)\"", panel, StringComparison.Ordinal);
        Assert.Contains("private Task LoadCandidatePromptAsync(CharacterIdentityBodyViewKey key, SceneAssetImage candidate)", panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shared composer's Generate Prompt must run the SAME LLM prompt compiler every other host uses — not a
    /// bespoke re-read of the deterministic prompt (operator request: "the body prompt is missing the prompt
    /// compiler"). The deterministic prompt is still resolved once, silently, when a row opens, and either way the
    /// textarea remounts through <c>BumpPromptRevision</c> so an uncontrolled input always shows the result.
    /// </summary>
    [Fact]
    public void GeneratePrompt_CompilesThroughTheSharedPromptCompiler_AndReplacesTheBox()
    {
        var panel = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        // The shared step wires its prompt-draft action to this panel's LLM compile.
        Assert.Contains("OnGeneratePrompt=\"() => GeneratePromptAsync(key)\"", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("disabled=\"@_busy\"", panel, StringComparison.Ordinal);

        // The compile resolves the canonical body text, the selected model's checkpoint profile and the prompt model,
        // then runs the shared compiler — the same sequence PromptAssetCreator and the other hosts run.
        Assert.Contains("private async Task GeneratePromptAsync(CharacterIdentityBodyViewKey key)", panel, StringComparison.Ordinal);
        Assert.Contains("BodyService.ResolveBodyTextsAsync(CharacterId, key.State)", panel, StringComparison.Ordinal);
        Assert.Contains("CompilerProfiles.FindByCheckpointAsync(model.ModelIdentifier)", panel, StringComparison.Ordinal);
        Assert.Contains("ModelResolutionService.ResolveImagePromptModelAsync()", panel, StringComparison.Ordinal);
        Assert.Contains("PromptCompiler.CompileAsync(new ImageCellCompileInput(", panel, StringComparison.Ordinal);

        // The deterministic prompt is still resolved once, silently, when a row opens.
        Assert.Contains("private async Task ResolvePromptAsync(CharacterIdentityBodyViewKey key)", panel, StringComparison.Ordinal);
        Assert.Contains("_reloading.Add(keyText)", panel, StringComparison.Ordinal);
        Assert.Contains("ResolvePromptAsync(key)", panel, StringComparison.Ordinal);

        // A resolve or compile re-keys the box so its contents are replaced rather than a stale edit staying visible.
        Assert.Contains("BumpPromptRevision(keyText)", panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// The five-second poll must never deaden an operator action (operator report 2026-09-22: "the genarate image
    /// button does not do anyting").
    ///
    /// The refresh lock is the panel's own housekeeping. It used to be shared with every row button
    /// (<c>disabled="@_busy"</c>) and with <c>RunAsync</c>'s opening guard, so while the poll was refreshing, clicks
    /// either landed on a disabled button or were discarded server-side with no message.
    /// </summary>
    [Fact]
    public void TheBackgroundRefresh_NeverGatesAnOperatorAction()
    {
        var panel = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        // No control anywhere is disabled by the refresh lock. These forms are CODE-only: the prose above them explains
        // the old bug and would otherwise satisfy the assertion on its own.
        Assert.DoesNotContain("disabled=\"@(_busy", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("_busy = true;", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("_busy = false;", panel, StringComparison.Ordinal);

        // Row actions are gated by their OWN row's in-flight state.
        Assert.Contains("disabled=\"@(IsSubmitting(keyText) || !CanGenerate)\"", panel, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@IsSubmitting(keyText)\"", panel, StringComparison.Ordinal);

        // The refresh keeps a lock of its own, so overlapping polls cannot stack up.
        Assert.Contains("private bool _refreshing;", panel, StringComparison.Ordinal);
        Assert.Contains("if (string.IsNullOrWhiteSpace(BuildId) || _refreshing)", panel, StringComparison.Ordinal);

        // ...and the row guard is the ROW's, so a second click on the same row is what it refuses.
        Assert.Contains("if (!_submitting.Add(keyText))", panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// The candidate deck opens on the NEWEST attempt (operator decision, 2026-09-22): the image being judged is the
    /// one just produced, not the first one ever made.
    /// </summary>
    [Fact]
    public void TheCandidateDeck_ListsTheNewestAttemptFirst()
    {
        var panel = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        Assert.Contains("OrderByDescending(image => image.CreatedUtc)", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderBy(image => image.CreatedUtc)", panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// The body view's image composition is the ONE shared step every other surface uses — the composer renders the
    /// Face, Body and Pose tabs, and the panel maps its bindings onto the body render instead of drawing its own
    /// identity and pose switches. The back view still takes no face, enforced in the submit path.
    /// </summary>
    [Fact]
    public void TheBodyView_ComposesThroughTheSharedImageStep()
    {
        var panel = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        // The shared composer, driven by the shared blueprint factory.
        Assert.Contains("<ImageStepComposer Blueprint=\"blueprint\"", panel, StringComparison.Ordinal);
        Assert.Contains("ImageStepBlueprintFactory.ForBodyView(new ImageStepActor(CharacterId, CharacterName))", panel, StringComparison.Ordinal);

        // The three slots map onto the body render's three conditionings.
        Assert.Contains("var face = FaceFromBindings(bindings);", panel, StringComparison.Ordinal);
        Assert.Contains("var body = BodyFromBindings(bindings);", panel, StringComparison.Ordinal);
        Assert.Contains("var pose = ImageStepPoseBinding.Resolve(bindings);", panel, StringComparison.Ordinal);
        Assert.Contains("bodyReference: body", panel, StringComparison.Ordinal);
        Assert.Contains("posePresetId: pose?.PresetId", panel, StringComparison.Ordinal);

        // The back view still takes no face reference.
        Assert.Contains("useIdentity: face is not null && key.View != SceneImageReferenceBodyView.Back", panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unclothed view shows its approved CLOTHED same-angle and offers a "remove clothes" edit from it, so the
    /// clothed/unclothed pairs match by angle (operator request) instead of every unclothed angle being derived from
    /// the unclothed front.
    /// </summary>
    [Fact]
    public void TheUnclothedViews_ShowTheApprovedClothedSameAngle_ForARemoveClothesEdit()
    {
        var panel = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        Assert.Contains("key.State == SceneImageReferenceBodyState.Unclothed", panel, StringComparison.Ordinal);
        Assert.Contains("AcceptedClothedAnglePreview(key)", panel, StringComparison.Ordinal);
        Assert.Contains("EditFromClothedSourceAsync(key)", panel, StringComparison.Ordinal);
        Assert.Contains("BodyService.EditFromClothedSourceAsync(", panel, StringComparison.Ordinal);
        Assert.Contains("Approved @ClothedAngleLabel(slot) — edit to remove clothes", panel, StringComparison.Ordinal);
        Assert.Contains("private string? AcceptedClothedAnglePreview(CharacterIdentityBodyViewKey key)", panel, StringComparison.Ordinal);

        // The approved clothed image also opens in the FULL shared editor, not only the one-shot remove-clothes request.
        Assert.Contains("private string? AcceptedClothedAngleEditUrl(CharacterIdentityBodyViewKey key)", panel, StringComparison.Ordinal);
        Assert.Contains("EditImageUrlFor(clothed)", panel, StringComparison.Ordinal);
        Assert.Contains("href=\"@clothedEditUrl\"", panel, StringComparison.Ordinal);
        // The full-editor link carries THIS (unclothed) view's candidate batch, so its result lands in this deck.
        Assert.Contains("batch={Uri.EscapeDataString(key.BatchIdFor(BuildId))}", panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// The body render conditions identity and body from the character's approved pack, so the shared step's Face and
    /// Body slots read those pack fields — never an approved scene asset, which the render cannot honour.
    /// </summary>
    [Fact]
    public void TheFaceAndBodySlots_ReadFromTheApprovedPack()
    {
        var panel = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        Assert.Contains("private ReferenceApplicationSelection? FaceFromBindings", panel, StringComparison.Ordinal);
        Assert.Contains("!string.IsNullOrWhiteSpace(binding.IdentityPackId)", panel, StringComparison.Ordinal);
        Assert.Contains("private SceneAssetBodyReferenceConditioning? BodyFromBindings", panel, StringComparison.Ordinal);
        Assert.Contains("new SceneAssetBodyReferenceConditioning(body.IdentityPackId!, body.ReferenceAssetId ?? string.Empty)", panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// The body view model and its choices are the panel's shared settings, threaded into the common step so its model
    /// picker reflects the same value every body view uses.
    /// </summary>
    [Fact]
    public void TheSharedStep_ThreadsTheBodyModelAndSettings()
    {
        var panel = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        Assert.Contains("ModelChoices=\"ModelChoices\"", panel, StringComparison.Ordinal);
        Assert.Contains("SelectedModelId=\"@ModelId\"", panel, StringComparison.Ordinal);
        Assert.Contains("ModelIdChanged.InvokeAsync(modelId)", panel, StringComparison.Ordinal);
        Assert.Contains("public IReadOnlyList<SceneImageModelChoice> ModelChoices { get; set; } = [];", panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every body view offers the character LoRA picker: the shared step's selections are wired back into the body
    /// render's LoRA conditioning, and a model change clears them exactly as it clears the reference bindings.
    /// </summary>
    [Fact]
    public void TheBodyView_OffersCharacterLoras_AndSendsThemToTheRender()
    {
        var panel = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        Assert.Contains("CharacterLoras=\"ViewLorasFor(keyText)\"", panel, StringComparison.Ordinal);
        Assert.Contains("CharacterLorasChanged=\"loras => SetViewLoras(keyText, loras)\"", panel, StringComparison.Ordinal);
        Assert.Contains("characterLoras: loras.Count > 0 ? loras : null", panel, StringComparison.Ordinal);
        Assert.Contains("_viewLoras.Clear();", panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every body view also offers the shared scene-LoRA picker (unlock / act / anatomy / style), because those are
    /// the LoRAs the body models actually carry (e.g. the Qwen-Image-2.1 unlock/anatomy catalog). Its selections reach
    /// the render's SceneLoras and clear on a model change, exactly like the character LoRAs.
    /// </summary>
    [Fact]
    public void TheBodyView_OffersSceneLoras_AndSendsThemToTheRender()
    {
        var panel = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        Assert.Contains("<SceneLoraPicker Family=\"@SelectedModelFamily\"", panel, StringComparison.Ordinal);
        Assert.Contains("Selections=\"ViewSceneLorasFor(keyText)\"", panel, StringComparison.Ordinal);
        Assert.Contains("SelectionsChanged=\"loras => SetViewSceneLoras(keyText, loras)\"", panel, StringComparison.Ordinal);
        Assert.Contains("sceneLoras: sceneLoras.Count > 0 ? sceneLoras : null", panel, StringComparison.Ordinal);
        Assert.Contains("_viewSceneLoras.Clear();", panel, StringComparison.Ordinal);
        Assert.Contains("private SceneImageModelFamily SelectedModelFamily", panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// The body view's Face / Body / Pose reference tabs must survive the LoRA pickers. The composer renders its
    /// character-LoRA picker INSTEAD of the reference tabs only for a model that declares the Lora reference strategy
    /// (its identity travels as a LoRA) - so a body view model that carries identity by reference (Qwen native
    /// references, FLUX conditioning) keeps its tabs, and the scene LoRAs live in the separate scene-LoRA picker.
    /// </summary>
    [Fact]
    public void TheBodyView_KeepsItsReferenceTabs_AlongsideTheLoraPickers()
    {
        var composer = Read("DreamGenClone.Web", "Components", "Shared", "ImageStepComposer.razor");

        // The LoRA picker gate is the model's own Lora strategy, not a per-step override that would hide references.
        Assert.Contains("Profile.Allows(ImageStepFeature.CharacterLoras) && CarriesLora && CharacterLorasChanged.HasDelegate", composer, StringComparison.Ordinal);
        // The reference tabs remain the else-branch, so a reference-carrying model always keeps them.
        Assert.Contains("else if (CarriesReferences)", composer, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePromotionGate_UsesTheOneSlotDefinition()
    {
        var source = Read("DreamGenClone.Web", "Application", "RolePlay", "CharacterIdentityPromotionService.cs");

        Assert.Contains("foreach (var required in CharacterIdentityBodySlots.All)", source, StringComparison.Ordinal);
        // The second table that used to live here is gone: two lists of slots can disagree, one cannot.
        Assert.DoesNotContain("RequiredBodySlots =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("(\"Clothed Front\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGrid_RendersTheCanonicalSlotsAndOffersEveryOneViewAction()
    {
        var source = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        // The rows come from the domain definition, both states.
        Assert.Contains("CharacterIdentityBodySlots.For(state)", source, StringComparison.Ordinal);
        Assert.Contains("SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyState.Unclothed", source, StringComparison.Ordinal);

        // Every action the plan names for a single view, and nothing that spans views.
        foreach (var action in new[]
                 {
                     "BodyService.GenerateAsync(",
                     "BodyService.EditFromAcceptedSourceAsync(",
                     "BodyService.EditFromClothedSourceAsync(",
                     "BodyService.UploadAsync(",
                     "BodyService.RecordSourceAsResultAsync(",
                     "BodyService.AcceptAsync(",
                     "BodyService.RecordFindingsAsync(",
                     "BodyService.RecordOverrideAsync(",
                     "BodyService.AnalyzeQualityAsync(",
                     "BodyService.ResolvePromptAsync(",
                     "BodyService.ListViewsAsync("
                 })
        {
            Assert.Contains(action, source, StringComparison.Ordinal);
        }

        // Quality evidence per view, and the findings the acceptance gate refuses on.
        Assert.Contains("view.QualityRating", source, StringComparison.Ordinal);
        Assert.Contains("view.Findings.NotPassed", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGrid_TakesItsModelAndSizeFromSettings_AndAsksForNeitherPerView()
    {
        var source = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        // B-122 E-2: the operator sets the body model and size ONCE, in the settings card; the grid reads them.
        Assert.Contains("public string ModelId { get; set; } = string.Empty;", source, StringComparison.Ordinal);
        Assert.Contains("public string ImageSize { get; set; } = string.Empty;", source, StringComparison.Ordinal);
        Assert.Contains("Body view settings", source, StringComparison.Ordinal);
        // No per-view typing and no model picker inside the grid.
        Assert.DoesNotContain("placeholder=\"e.g. 1024x1536\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ListSceneImageModelsAsync", source, StringComparison.Ordinal);
        // The gate that makes an unset pair visible instead of inert.
        Assert.Contains("private bool CanGenerate", source, StringComparison.Ordinal);

        var studio = Read("DreamGenClone.Web", "Components", "Pages", "CharacterStudio.razor");
        Assert.Contains("BodyService.ResolveViewSettingsAsync(IdentityKey)", studio, StringComparison.Ordinal);
        Assert.Contains("SaveBodyViewSettingsAsync", studio, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGrid_OpensEveryViewFullSize_NotJustAThumbnail()
    {
        var source = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        // A thumbnail is a signpost, not a review surface: every view opens at full size.
        Assert.Contains("OpenViewer(", source, StringComparison.Ordinal);
        Assert.Contains("modal-dialog modal-xl", source, StringComparison.Ordinal);
        Assert.Contains("max-height:78vh", source, StringComparison.Ordinal);
        Assert.Contains("_viewer.Url", source, StringComparison.Ordinal);
        Assert.Contains("cursor:zoom-in", source, StringComparison.Ordinal);

        // With the source beside it when the view was an edit, and the prompt it came from.
        Assert.Contains("_viewer.SourceUrl", source, StringComparison.Ordinal);
        Assert.Contains("shown.ResolvedPromptText", source, StringComparison.Ordinal);
        Assert.Contains("Open the raw file", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGrid_CompletesAViewWhenItsRenderFinishes_AndRefreshesItself()
    {
        var source = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");

        // A render finishes on the container's image row, not on the view: without this the row sits at Pending
        // forever and the operator sees "the button did nothing" (B-122 E-2, 2026-09-22).
        Assert.Contains("BodyService.RecordResultAsync(BuildId, view.Key(), view.OutputArtifactId!)", source, StringComparison.Ordinal);
        Assert.Contains("SceneAssetStatus.Complete", source, StringComparison.Ordinal);
        // And it polls while something is rendering, so the image appears without a manual refresh.
        Assert.Contains("new Timer(", source, StringComparison.Ordinal);
        Assert.Contains("CharacterIdentityAngleStatus.Pending", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGrid_HasNoBatchAffordance()
    {
        var source = Read("DreamGenClone.Web", "Components", "RolePlay", "BodyViewsPanel.razor");
        var handlers = System.Text.RegularExpressions.Regex
            .Matches(source, "@onclick=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToList();

        Assert.NotEmpty(handlers);
        foreach (var forbidden in new[] { "all", "every", "sweep", "batch" })
        {
            Assert.DoesNotContain(handlers, handler => handler.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void TheStudio_HandsTheBodyBuildAndTheResolvedTemplateNameToTheGrid()
    {
        var source = Read("DreamGenClone.Web", "Components", "Pages", "CharacterStudio.razor");

        Assert.Contains("<BodyViewsPanel BuildId=\"@_bodyBuild.Id\"", source, StringComparison.Ordinal);
        Assert.Contains("ModelId=\"@_bodyViewModelId\"", source, StringComparison.Ordinal);
        Assert.Contains("ImageSize=\"@_bodyViewImageSize\"", source, StringComparison.Ordinal);
        // The prompts address the character TEMPLATE's name through the resolver, not the route id.
        Assert.Contains("private string BodyPanelCharacterName => _owner?.TemplateName", source, StringComparison.Ordinal);
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