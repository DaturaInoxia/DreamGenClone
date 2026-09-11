using System.Text.RegularExpressions;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Source-contract checks for the production studio Razor markup. These tests intentionally do not
/// render a Blazor component; the solution has no component test harness, so the final Razor build
/// remains the executable compiler validation for the page.
/// </summary>
public sealed class SceneImageStudioUiContractTests
{
    private static readonly string Source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Pages", "SceneImageStudio.razor"));
    private static readonly string ProductionWorkspaceSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Pages", "ProductionWorkspace.razor"));
    private static readonly string RunTraySource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Shared", "RunTray.razor"));
    private static readonly string CompositionComposerSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Pages", "CompositionComposer.razor"));
    private static readonly string SceneImageEditorSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Pages", "SceneImageEditor.razor"));
    private static readonly string SceneImageEditorStylesheet = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Pages", "SceneImageEditor.razor.css"));
    private static readonly string SceneImageGallerySource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Pages", "SceneImageGallery.razor"));
    private static readonly string SceneImageGalleryStylesheet = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Pages", "SceneImageGallery.razor.css"));
    private static readonly string EditIterateWorkbenchSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Shared", "EditIterateWorkbench.razor"));
    private static readonly string SceneImageStudioStylesheet = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Pages", "SceneImageStudio.razor.css"));
    private static readonly string SceneImageServiceSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Application", "RolePlay", "SceneImageService.cs"));
    private static readonly string SceneImageEditingJobHandlerSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Application", "RolePlay", "SceneImageEditingJobHandler.cs"));

    [Fact]
    public void ProductionStudio_HandsCompositionToTheDedicatedComposer()
    {
        var studioStart = IndexOf("<div class=\"card mb-3 scene-production-studio\">");
        var povSection = IndexOf("<div class=\"scene-production-pov mb-3\">", studioStart);
        var povGate = IndexOf("@if (!string.IsNullOrWhiteSpace(_productionPov))", povSection);
        var createCommand = IndexOf("@onclick=\"CreateOrLoadProductionAsync\"", povGate);
        Assert.True(povSection < povGate && povGate < createCommand,
            "Open Composition Composer must appear in the Production POV section once a POV is selected.");
        Assert.Single(Regex.Matches(Source, "@onclick=\"CreateOrLoadProductionAsync\"", RegexOptions.CultureInvariant).Cast<Match>());
        Assert.Contains("Open Composition Composer", Source, StringComparison.Ordinal);
        Assert.Contains("Nav.NavigateTo($\"/roleplay/studio/{sessionId}/{interactionId}/production/{_productionGroup.Id}/composition\")", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("@onclick=\"GenerateProductionCompositionAsync\"", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("@bind=\"_selectedProductionModelId\"", Source, StringComparison.Ordinal);

        Assert.Contains("@page \"/roleplay/studio/{sessionId}/{interactionId}/production/{productionGroupId}/composition\"", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"GeneratePromptAsync\"", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"GenerateCompositionAsync\"", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("@bind=\"_selectedModelId\"", CompositionComposerSource, StringComparison.Ordinal);
        // B-111 composer rework: real-payload prompt-input inspector (edit/remove per element, remove
        // whole characters) with the approved-reference-image panel, plus per-attempt actions.
        Assert.Contains("Selected Moment &amp; production context", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("Prompt input (what the compiler receives)", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("BuildPromptOverrides", CompositionComposerSource, StringComparison.Ordinal);
        // Removal uses two-way binding (the previous checked + @onchange pattern inverted the logic),
        // and removed characters are derived from the bound group state.
        Assert.Contains("@bind=\"row.Removed\"", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("@bind=\"group.Removed\"", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("RemovedCharacterKeys", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("ReferenceApplyPanel", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("SetCompositionDispositionAsync", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("ApproveCompositionAttemptAsync", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("ConfirmDeleteCompositionAsync", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("Regenerate Sibling", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("OnModelChangedAsync", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("ProductionGroupId = _productionGroup!.Id", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("CompiledMediaBriefId = _compiledMediaBrief!.Id", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("SceneImageProductionStage.Composition", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("Take(100)", CompositionComposerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletedMomentEnrichment_OpensProductionStudioBeforePovSelection()
    {
        Assert.Contains("@page \"/roleplay/studio/{sessionId}/{interactionId}/production/moment/{momentEnrichmentId}\"", Source, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"OpenProductionStudioForMoment\"", Source, StringComparison.Ordinal);
        Assert.Contains("private async Task RestoreProductionMomentSelectionAsync(string enrichmentId)", Source, StringComparison.Ordinal);
        Assert.Contains("MomentEnrichmentService.GetAsync(enrichmentId)", Source, StringComparison.Ordinal);
        Assert.Contains("?momentEnrichmentId={Uri.EscapeDataString(ActiveMomentEnrichmentId)}", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("_productionPov = SceneImagePovFramer.Omniscient;", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentGeneration_UsesDurableWorkspaceAndPreservesExistingGenerationSurfaces()
    {
        Assert.Contains("<RunTray SessionId=\"@sessionId\" InteractionId=\"@interactionId\" MomentEnrichmentId=\"@(_productionGroup?.MomentEnrichmentId ?? _momentEnrichment?.Id)\" />", Source, StringComparison.Ordinal);
        Assert.Contains("SceneImageProductionSchema.CurrentGeneration", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("<div hidden=\"@IsCurrentProductionSession\">", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("scene-image-legacy-tools\" hidden=\"@IsCurrentProductionSession\"", Source, StringComparison.Ordinal);
        Assert.Contains("@if (!IsSelectedMomentEnriched)", Source, StringComparison.Ordinal);
        Assert.Contains("Composition Composer", Source, StringComparison.Ordinal);
        Assert.Contains("@bind=\"_selectedGenericModelId\"", Source, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"() => OpenImageEditor(img)\"", Source, StringComparison.Ordinal);
        Assert.Contains("This session predates the current production schema. Create a new session", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionStudio_SeparatesPovWorkbenchFromReusableDurableJobsTray()
    {
        Assert.Contains("ProductionStudioTab.ProductionPov", Source, StringComparison.Ordinal);
        Assert.Contains("ProductionStudioTab.Jobs", Source, StringComparison.Ordinal);
        Assert.Contains("<RunTray SessionId=\"@sessionId\" InteractionId=\"@interactionId\" MomentEnrichmentId=\"@(_productionGroup?.MomentEnrichmentId ?? _momentEnrichment?.Id)\" />", Source, StringComparison.Ordinal);
        Assert.Contains("public string? MomentEnrichmentId", RunTraySource, StringComparison.Ordinal);
        Assert.Contains("ProductionGroupRepository.ListByInteractionAsync(SessionId, InteractionId)", RunTraySource, StringComparison.Ordinal);
        Assert.Contains("group.MomentEnrichmentId, MomentEnrichmentId", RunTraySource, StringComparison.Ordinal);
        Assert.Contains("ImageService.ListImagesByProductionGroupAsync(group.Id)", RunTraySource, StringComparison.Ordinal);
        Assert.Contains("JobRepository.ListRecentAsync(200)", RunTraySource, StringComparison.Ordinal);
        Assert.Contains("Take(100)", RunTraySource, StringComparison.Ordinal);
        Assert.Contains("JobQueue.TryActivateAsync(jobId, DateTime.UtcNow)", RunTraySource, StringComparison.Ordinal);
        Assert.Contains("JobQueue.TryCancelAsync(jobId, cancelledUtc)", RunTraySource, StringComparison.Ordinal);
        Assert.Contains("ImageService.TryCancelImageAsync(SessionId, imageRecordId, cancelledUtc)", RunTraySource, StringComparison.Ordinal);
        Assert.Contains("<th>Target</th>", RunTraySource, StringComparison.Ordinal);
        Assert.Contains("ModelRepository.GetByIdAsync(attempt.RequestedModelId)", RunTraySource, StringComparison.Ordinal);
        Assert.Contains("ProviderRepository.GetByIdAsync(model.ProviderId)", RunTraySource, StringComparison.Ordinal);
        Assert.Contains("model?.DisplayName", RunTraySource, StringComparison.Ordinal);
        Assert.Contains("DurableBackgroundJobStatus.Staged", RunTraySource, StringComparison.Ordinal);
        Assert.Contains("Confirm cancel", RunTraySource, StringComparison.Ordinal);
        Assert.Contains("Queue payload", RunTraySource, StringComparison.Ordinal);
    }

    [Fact]
    public void StoryArcTurnSelection_NavigatesToTurnOnFirstClick()
    {
        var selection = IndexOf("private void SelectArcTurn(StoryArcTurn turn)");
        var navigation = IndexOf("NavigateToTurn(turn);", selection);

        Assert.True(selection < navigation,
            "Selecting a Story Arc turn must navigate immediately instead of requiring a second click.");
    }

    [Fact]
    public void DurableWorkspace_PreservesSelectionAndExposesExactOperationalWorkflow()
    {
        Assert.Contains("_selectedWorkloadId", ProductionWorkspaceSource, StringComparison.Ordinal);
        Assert.Contains("_selectedWorkloadItemId", ProductionWorkspaceSource, StringComparison.Ordinal);
        Assert.Contains("_selectedAttemptId", ProductionWorkspaceSource, StringComparison.Ordinal);
        Assert.Contains("new System.Threading.Timer", ProductionWorkspaceSource, StringComparison.Ordinal);
        Assert.Contains("UpdatePolling();", ProductionWorkspaceSource, StringComparison.Ordinal);
        Assert.Contains("_comparisonAttemptIds", ProductionWorkspaceSource, StringComparison.Ordinal);
        Assert.Contains("The selected Moment and model settings are prepared automatically", ProductionWorkspaceSource, StringComparison.Ordinal);
        Assert.Contains("Nothing to fill in here.", ProductionWorkspaceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Binding snapshot JSON", ProductionWorkspaceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Compiler settings JSON", ProductionWorkspaceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("@onclick=\"PrepareRevisionAsync\"", ProductionWorkspaceSource, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"SubmitSelectedAsync\"", ProductionWorkspaceSource, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"BeginCancelConfirmation\"", ProductionWorkspaceSource, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"RetrySelectedAsync\"", ProductionWorkspaceSource, StringComparison.Ordinal);
        Assert.Contains("ReviewSelectedAsync", ProductionWorkspaceSource, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"ApproveSelectedAsync\"", ProductionWorkspaceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Identity_IsSurfacedAsASeparateStageWithExplicitSkipAndFinishState()
    {
        Assert.Contains("2. Identity", Source, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"ApplyProductionIdentityAsync\"", Source, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"SkipProductionIdentityAsync\"", Source, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"ClearProductionIdentitySkipAsync\"", Source, StringComparison.Ordinal);
        Assert.Contains("<strong>3. Finish</strong>", Source, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"EnqueueProductionFinishAsync\"", Source, StringComparison.Ordinal);
        Assert.Contains("Change class (required)", Source, StringComparison.Ordinal);
        Assert.Contains("ToggleProductionCompare", Source, StringComparison.Ordinal);
        Assert.Contains("ToggleProductionCompareAttempt", Source, StringComparison.Ordinal);
        Assert.Contains("Branch a sibling Composition", Source, StringComparison.Ordinal);
        Assert.Contains("Identity readiness", Source, StringComparison.Ordinal);
        Assert.Contains("Identity blocked:", Source, StringComparison.Ordinal);
        Assert.Contains("CanonicalFaceAssetId", Source, StringComparison.Ordinal);
        Assert.Contains("Request adult-content Finish edit", Source, StringComparison.Ordinal);
        Assert.Contains("resolved editor model", Source, StringComparison.Ordinal);
        Assert.Contains("RequestAdultContent = _productionFinishAdultContent", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("Character Identity (one-pass)", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("@onclick=\"GenerateProductionCompositionWithIdentityAsync\"", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerateProductionIdentityAsync", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerateProductionFinishAsync", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyProductionIdentity_UsesTheProductionIdentityOperationInsteadOfHiddenPackSelection()
    {
        var methodStart = IndexOf("private async Task ApplyProductionIdentityAsync()");
        var methodEnd = IndexOf("private async Task SkipProductionIdentityAsync()", methodStart);
        var method = Source[methodStart..methodEnd];

        Assert.Contains("Instruction = \"Face-only identity correction.\"", method, StringComparison.Ordinal);
        Assert.Contains("ReferenceApplications = _productionReferenceApplications", method, StringComparison.Ordinal);
        Assert.Contains("Image 1 is the existing scene and must remain the base image.", SceneImageServiceSource, StringComparison.Ordinal);
        Assert.Contains("additional approved face images are identity references only, not replacement images or composition sources.", SceneImageServiceSource, StringComparison.Ordinal);
        Assert.Contains("Treat the existing scene's visible neck and body skin tone as authoritative", SceneImageServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Select at least one approved identity pack", method, StringComparison.Ordinal);
        Assert.DoesNotContain("_productionIdentityPacks.Where(option => option.Selected)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void AttemptExecutionDispositionAndApproval_AreRenderedSeparately()
    {
        Assert.Contains("<dt>Execution status</dt>", Source, StringComparison.Ordinal);
        Assert.Contains("<dt>Disposition</dt>", Source, StringComparison.Ordinal);
        Assert.Contains("<dt>Approval</dt>", Source, StringComparison.Ordinal);

        var attemptLoop = IndexOf("@foreach (var attempt in stageAttempts)");
        var attemptEnd = IndexOf("</section>", attemptLoop);
        var attemptMarkup = Source[attemptLoop..attemptEnd];
        Assert.Contains("@attempt.Status", attemptMarkup, StringComparison.Ordinal);
        Assert.Contains("@attempt.Disposition", attemptMarkup, StringComparison.Ordinal);
        Assert.Contains("? \"Approved\" : \"Not approved\"", attemptMarkup, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionAttempts_ShowEveryStageAcrossTheFullWorkspaceWithAnEditorLink()
    {
        Assert.Contains("new[] { SceneImageProductionStage.Composition, SceneImageProductionStage.Identity, SceneImageProductionStage.Finish }", Source, StringComparison.Ordinal);
        Assert.Contains("href=\"/roleplay/image-editor/@sessionId/@interactionId/@attempt.Id\"", Source, StringComparison.Ordinal);
        Assert.Contains("var editAttempts = _productionAttempts", Source, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Image edits\"", Source, StringComparison.Ordinal);
        Assert.Contains("title=\"Open this edit in the image editor\"", Source, StringComparison.Ordinal);
        Assert.Contains(".scene-production-attempts {\r\n    grid-row: 6;", SceneImageStudioStylesheet, StringComparison.Ordinal);
        Assert.Contains(".scene-production-canvas-actions,\r\n.scene-production-attempts {\r\n    grid-column: 1 / -1;", SceneImageStudioStylesheet, StringComparison.Ordinal);
    }

    [Fact]
    public void SceneImageEditor_AutomaticallyAnalyzesSourceAndExposesNativeQwenReferences()
    {
        Assert.Contains("await CompilationService.EnqueueDescriptionAsync(_editSession.Id);", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.Contains("_descriptionPending = true;", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.Contains("EditorReferenceStrategies = [\"TextOnly\", \"NativeMultiReference\"]", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.Contains("EditorModelId = _selectedEditorModelId!", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.Contains("ReferenceApplications = _referenceApplications", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.Contains("_sourceImage = routedImage;", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.Contains("SourceImageId = _sourceImage.Id", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.Contains("_lineageRootId = FindLineageRootId(images, _sourceImage)", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.Contains("_lineage = BuildLineage(images, _lineageRootId ?? _sourceImage.Id)", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.Contains("private static string FindLineageRootId", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.Contains("image.Status == SceneImageStatus.Complete && !string.IsNullOrWhiteSpace(image.FileRelativePath)", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.Contains("class=\"scene-edit-lineage-thumb\"", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.Contains("Exact edit prompt", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.Contains("@image.PromptSnapshot", SceneImageEditorSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SceneImageEditor_RunsEditsInPlaceInsteadOfReturningToTheStudio()
    {
        Assert.DoesNotContain("Nav.NavigateTo($\"/roleplay/studio/{sessionId}/{interactionId}\"", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.Contains("_statusMessage = \"Image edit queued.\";", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.Contains("_statusMessage = \"Identity correction queued.\";", SceneImageEditorSource, StringComparison.Ordinal);
        // Both run actions must keep the page polling so the result renders in the in-place lineage.
        Assert.Matches("_statusMessage = \"Image edit queued\\.\";\\s*EnsurePolling\\(\\);", SceneImageEditorSource);
        Assert.Matches("_statusMessage = \"Identity correction queued\\.\";\\s*EnsurePolling\\(\\);", SceneImageEditorSource);
        Assert.Contains("private SceneImageRecord? ResolveResultImage(IReadOnlyList<SceneImageRecord> images)", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.Contains("_resultImage = ResolveResultImage(images);", SceneImageEditorSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SceneImageEditor_FaceReferencePickerListsApprovedFacesAsThumbnails()
    {
        var pickerStart = SceneImageEditorSource.IndexOf("class=\"scene-face-picker-toggle\"", StringComparison.Ordinal);
        var pickerEnd = SceneImageEditorSource.IndexOf("</div>", SceneImageEditorSource.IndexOf("scene-face-picker-menu", pickerStart, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(pickerStart > 0 && pickerEnd > pickerStart, "The face reference picker markup must exist.");
        var pickerMarkup = SceneImageEditorSource[pickerStart..pickerEnd];
        Assert.Contains("/scene-images/@face.FileRelativePath", pickerMarkup, StringComparison.Ordinal);
        Assert.Contains("class=\"scene-face-picker-thumb\"", pickerMarkup, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"() => SelectIdentityFaceAsync(target, face.Id)\"", pickerMarkup, StringComparison.Ordinal);
        Assert.Contains("@IdentityFaceLabel(face)", pickerMarkup, StringComparison.Ordinal);
        Assert.Contains(".scene-face-picker-thumb {", SceneImageEditorStylesheet, StringComparison.Ordinal);
    }

    [Fact]
    public void SceneImageGallery_OpensStudioAtTheImageProductionAndUsesFullSizeActions()
    {
        // "Open in studio" must land on the image's own production lineage, not the legacy studio.
        Assert.Contains("/roleplay/studio/{sessionId}/{image.InteractionId}/production/{image.ProductionGroupId}", SceneImageGallerySource, StringComparison.Ordinal);
        Assert.Contains("/roleplay/studio/{sessionId}/{image.InteractionId}/production/moment/{image.MomentEnrichmentId}", SceneImageGallerySource, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"() => OpenStudio(img)\"", SceneImageGallerySource, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenStudio(group.InteractionId)", SceneImageGallerySource, StringComparison.Ordinal);
        // The card actions must be labelled, full-size buttons (the icon-only py-0/px-1 stubs rendered as slivers).
        Assert.Contains(">Edit</button>", SceneImageGallerySource, StringComparison.Ordinal);
        Assert.Contains(">Studio</button>", SceneImageGallerySource, StringComparison.Ordinal);
        Assert.Contains(">Delete</button>", SceneImageGallerySource, StringComparison.Ordinal);
        Assert.DoesNotContain("py-0 px-1", SceneImageGallerySource, StringComparison.Ordinal);
        Assert.Contains("class=\"scene-gallery-actions\"", SceneImageGallerySource, StringComparison.Ordinal);
        Assert.Contains(".scene-gallery-actions .btn {", SceneImageGalleryStylesheet, StringComparison.Ordinal);
    }

    [Fact]
    public void EditIterateWorkbench_ForwardsBoundFieldsToItsParent()
    {
        Assert.Contains("@bind:set=\"SetIntentAsync\"", EditIterateWorkbenchSource, StringComparison.Ordinal);
        Assert.Contains("await IntentChanged.InvokeAsync(value);", EditIterateWorkbenchSource, StringComparison.Ordinal);
        Assert.Contains("await ClarificationChanged.InvokeAsync(value);", EditIterateWorkbenchSource, StringComparison.Ordinal);
        Assert.Contains("await EditablePromptChanged.InvokeAsync(value);", EditIterateWorkbenchSource, StringComparison.Ordinal);
    }

    [Fact]
    public void StagedWorkbench_ExposesReadinessParentSelectionAndSkipDialog()
    {
        Assert.Contains("StageState(SceneImageProductionStage.Composition)", Source, StringComparison.Ordinal);
        Assert.Contains("CompletedProductionAttempts()", Source, StringComparison.Ordinal);
        Assert.Contains("OpenIdentityEdit", Source, StringComparison.Ordinal);
        Assert.Contains("only to selected faces", Source, StringComparison.Ordinal);
        Assert.Contains("ReadyIdentityCount(characters)", Source, StringComparison.Ordinal);
        Assert.Contains("IdentityReadinessFor(character.Name)", Source, StringComparison.Ordinal);
        Assert.Contains("EligibleFinishParents()", Source, StringComparison.Ordinal);
        Assert.Contains("OpenIdentitySkipDialogAsync", Source, StringComparison.Ordinal);
        Assert.Contains("aria-modal=\"true\"", Source, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@(_busy || string.IsNullOrWhiteSpace(_productionIdentitySkipReason))\"", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void AttemptTreeAndCompare_ExposeLineageMarkersAndPersistedDifferences()
    {
        Assert.Contains("has-parent", Source, StringComparison.Ordinal);
        Assert.Contains("BranchFromAttemptAsync", Source, StringComparison.Ordinal);
        Assert.Contains("Identity-stale", Source, StringComparison.Ordinal);
        Assert.Contains("CompareDifferences()", Source, StringComparison.Ordinal);
        Assert.Contains("IdentityReferenceBindingsJson", Source, StringComparison.Ordinal);
        Assert.Contains("Reference image {selected.Ordinal + 1}", SceneImageServiceSource, StringComparison.Ordinal);
        Assert.Contains("selected face identity reference for {binding.CharacterName} (CharacterFace:{binding.CharacterId})", SceneImageEditingJobHandlerSource, StringComparison.Ordinal);
        Assert.Contains("Attempt @", Source, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Attempt @(compareIndex == 0 ? \"A\" : \"B\")\"", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ApprovalSurface_StatesGateReasonAndRequiresExactCompletedAttempt()
    {
        Assert.Contains("ApprovalReason(_selectedProductionAttempt)", Source, StringComparison.Ordinal);
        Assert.Contains("CanApprove(_selectedProductionAttempt)", Source, StringComparison.Ordinal);
        Assert.Contains("Identity required - this attempt has no completed identity pass.", Source, StringComparison.Ordinal);
        Assert.Contains("new decision version", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void StagedWorkflow_PreservesStateKeysAndSupportsCompareApprovalAndFocusRules()
    {
        Assert.Contains("_activeProductionStage", Source, StringComparison.Ordinal);
        Assert.Contains("_selectedParentAttemptId", Source, StringComparison.Ordinal);
        Assert.Contains("_compareAttemptIds", Source, StringComparison.Ordinal);
        Assert.Contains("_canvasMode", Source, StringComparison.Ordinal);
        Assert.Contains("_productionFinishChangeClass", Source, StringComparison.Ordinal);
        Assert.Contains("_identitySkipDialogOpen", Source, StringComparison.Ordinal);
        Assert.Contains("PreserveProductionStateKeys", Source, StringComparison.Ordinal);
        Assert.Contains("Approve Attempt @(compareIndex == 0 ? \"A\" : \"B\")", Source, StringComparison.Ordinal);
        Assert.Contains("await focusable[_identitySkipFocusIndex].FocusAsync()", Source, StringComparison.Ordinal);
        Assert.Contains("await _identitySkipOpener.FocusAsync()", Source, StringComparison.Ordinal);
        Assert.Contains("IsStageAvailable(stages[nextIndex])", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void AdditionalPhase2Tools_ExcludeProductionAttemptsAndUseCurrentTerminology()
    {
        var productionStudio = IndexOf("scene-production-studio");
        var additionalSection = IndexOf("Additional Phase 2 Tools", productionStudio);
        var additionalImages = IndexOf("<strong>Additional Images</strong>", additionalSection);
        var additionalLoop = IndexOf("@foreach (var img in _additionalImages)", additionalImages);
        Assert.True(productionStudio < additionalSection && additionalSection < additionalImages && additionalImages < additionalLoop,
            "Additional Phase 2 tools and their image loop must remain after the production studio.");
        Assert.Contains("<strong>Identity-Conditioned Render</strong>", Source, StringComparison.Ordinal);
        Assert.Contains("private IEnumerable<SceneImageRecord> _additionalImages", Source, StringComparison.Ordinal);
        Assert.Contains("_images.Where(image => string.IsNullOrWhiteSpace(image.ProductionGroupId))", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("@foreach (var img in _images)", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateBeats_IsCanonicalCatalogueWriteAndHistoricalSchemaV3IsReadOnly()
    {
        var catalogueStart = IndexOf("<div class=\"card mb-3 scene-beat-catalogue\">");
        var historicalStart = IndexOf("<strong>Historical Image Plan", catalogueStart);
        var canonicalSection = Source[catalogueStart..historicalStart];
        Assert.Contains("> Generate Beats", canonicalSection, StringComparison.Ordinal);
        Assert.Contains("BeatPipelineService.EnqueueCatalogueAsync", Source, StringComparison.Ordinal);

        var historicalEnd = IndexOf("<div class=\"card mb-3\">", historicalStart + 1);
        var historicalSection = Source[historicalStart..historicalEnd];
        Assert.Contains("schema v3, read-only", historicalSection, StringComparison.Ordinal);
        Assert.Contains("Historical schema-v3 data remains visible", historicalSection, StringComparison.Ordinal);
        Assert.DoesNotContain("Prepare Legacy Prompt Input", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("ImageService.EnqueueBeatAnalysisAsync", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerateBeatsAsync", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionPrompt_UsesExactGroupAndCompiledStillBrief()
    {
        Assert.Contains("ProductionService.GetOrCreateStillBriefAsync(_productionGroup.Id)", Source, StringComparison.Ordinal);
        Assert.Contains("ImageService.GetLatestCompletedProductionPromptAsync(", Source, StringComparison.Ordinal);
        Assert.Contains("ProductionGroupId = _productionGroup.Id", Source, StringComparison.Ordinal);
        Assert.Contains("CompiledMediaBriefId = _compiledMediaBrief.Id", Source, StringComparison.Ordinal);
        Assert.Contains("Pov = _productionGroup.Pov", Source, StringComparison.Ordinal);
        Assert.Contains("capability.ProviderKey", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoricalBeatReadAndProductionGroupLineageContractsRemainPresent()
    {
        var root = FindRepositoryRoot();
        var repositoryContract = File.ReadAllText(Path.Combine(
            root, "DreamGenClone.Application", "RolePlay", "ISceneImageRepository.cs"));
        var imageService = File.ReadAllText(Path.Combine(
            root, "DreamGenClone.Web", "Application", "RolePlay", "SceneImageService.cs"));

        Assert.Contains("GetBeatAnalysisByTurnAsync", repositoryContract, StringComparison.Ordinal);
        Assert.Contains("_repository.GetBeatAnalysisByTurnAsync", imageService, StringComparison.Ordinal);
        Assert.Contains("ValidateCanonicalProductionAsync", imageService, StringComparison.Ordinal);
        Assert.Contains("EnsureBriefMatchesGroup", imageService, StringComparison.Ordinal);
        Assert.Contains("ExtractTypedReferences(canonical.Brief)", imageService, StringComparison.Ordinal);
        Assert.Contains("ProductionGroupId = productionGroup?.Id", imageService, StringComparison.Ordinal);
        Assert.Contains("CatalogueId = productionGroup?.CatalogueId", imageService, StringComparison.Ordinal);
        Assert.Contains("MomentEnrichmentId = productionGroup?.MomentEnrichmentId", imageService, StringComparison.Ordinal);
        Assert.Contains("ProductionStage = productionGroup is null ? null", imageService, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericSceneImageEdit_DoesNotEnterProductionFinishWorkflow()
    {
        var imageService = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "DreamGenClone.Web", "Application", "RolePlay", "SceneImageService.cs"));
        var genericEditStart = imageService.IndexOf("public async Task<SceneImageRecord> EnqueueEditAsync", StringComparison.Ordinal);
        var genericEditEnd = imageService.IndexOf("public Task<SceneImagePromptRecord?> GetPromptAsync", genericEditStart, StringComparison.Ordinal);

        Assert.True(genericEditStart >= 0 && genericEditEnd > genericEditStart,
            "Could not isolate SceneImageService.EnqueueEditAsync.");
        var genericEditSource = imageService[genericEditStart..genericEditEnd];
        Assert.Contains("ProductionGroupId = source.ProductionGroupId", genericEditSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductionStage = SceneImageProductionStage.Finish", genericEditSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Disposition = SceneImageAttemptDisposition.Active", genericEditSource, StringComparison.Ordinal);
    }

    private static int IndexOf(string value, int startIndex = 0)
    {
        var index = Source.IndexOf(value, startIndex, StringComparison.Ordinal);
        Assert.True(index >= 0, $"SceneImageStudio.razor is missing expected contract marker: {value}");
        return index;
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "DreamGenClone.sln"))
                && File.Exists(Path.Combine(current.FullName, "Directory.Build.props")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not find the DreamGenClone repository root from '{AppContext.BaseDirectory}'.");
    }
}