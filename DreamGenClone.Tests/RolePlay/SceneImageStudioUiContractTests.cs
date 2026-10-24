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
        FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Editing", "ImageEditWorkspace.razor"));
    private static readonly string SceneImageEditorStylesheet = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Editing", "ImageEditWorkspace.razor.css"));
    private static readonly string SceneImageEditAdapterSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Application", "RolePlay", "Editing", "SceneImageEditWorkspaceService.cs"));
    private static readonly string SceneImageGallerySource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Pages", "SceneImageGallery.razor"));
    private static readonly string EditIterateWorkbenchSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Shared", "EditIterateWorkbench.razor"));
    private static readonly string SceneImageStudioStylesheet = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Pages", "SceneImageStudio.razor.css"));
    private static readonly string SceneImageServiceSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Application", "RolePlay", "SceneImageService.cs"));
    private static readonly string SceneImageEditingJobHandlerSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Application", "RolePlay", "SceneImageEditingJobHandler.cs"));
    private static readonly string ComposerSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Shared", "ImageStepComposer.razor"));

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
        // B-130: the model and the reference slots are the ONE composer's, so the page no longer binds the model itself
        // and no longer owns a per-element strategy list. What it still owns is the prompt flow and the inspector.
        Assert.Contains("ImageStepComposer Blueprint=\"CompositionBlueprint\"", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("ForPackIdentityComposition", CompositionComposerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("@bind=\"_selectedModelId\"", CompositionComposerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecutableStrategies=", CompositionComposerSource, StringComparison.Ordinal);
        // B-111 composer rework: real-payload prompt-input inspector (edit/remove per element, remove
        // whole characters) with the approved-reference-image panel, plus per-attempt actions.
        Assert.Contains("Selected Moment &amp; production context", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("Prompt input (what the compiler receives)", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("BuildPromptOverrides", CompositionComposerSource, StringComparison.Ordinal);
        // Every inspector row must address a member the payload ACTUALLY has. "Visual description" was keyed
        // `moment.visualDescription`, which the compiled brief does not contain, so the box was always empty and typing
        // into it wrote a property nothing reads (reported 2026-10-22). The element keys now live in the ONE cluster
        // table (PromptElementClusters) the inspector reads, so the contract here is that the page consumes that table
        // and never names a key the table does not own.
        Assert.Contains("PromptElementClusters.Scene", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("AddClusterRow", CompositionComposerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AddPromptRow", CompositionComposerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("moment.visualDescription", CompositionComposerSource, StringComparison.Ordinal);
        // Removal is a CASCADE, not two-way binding: the whole-character tick must drive every section, and releasing a
        // section must release the character, or the two controls disagree (the group tick silently swallowed the
        // row untick in BuildPromptOverrides).
        Assert.Contains("SetGroupRemoved(group, (bool?)args.Value == true)", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("SetRowRemoved(group, row, (bool?)args.Value == true)", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("private void SetGroupRemoved(PromptGroup group, bool removed)", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("private void SetRowRemoved(PromptGroup group, PromptFieldRow row, bool removed)", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("RemovedCharacterKeys", CompositionComposerSource, StringComparison.Ordinal);
        // B-130: the approved-reference panel is replaced by the ONE composer, which presents the step's slot list.
        Assert.Contains("ImageStepComposer Blueprint=\"CompositionBlueprint\"", CompositionComposerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ReferenceApplyPanel", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("SetCompositionDispositionAsync", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("ApproveCompositionAttemptAsync", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("ConfirmDeleteCompositionAsync", CompositionComposerSource, StringComparison.Ordinal);
        // The step's own action rides in the card's footer now, so the marker is the command and its title rather than a
        // bespoke button label.
        Assert.Contains("RegenerateCompositionAsync", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("title=\"Regenerate a sibling Composition from this image's inputs\"", CompositionComposerSource, StringComparison.Ordinal);
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
        Assert.Contains("Choices=\"_genericModelChoices\"", Source, StringComparison.Ordinal);
        Assert.Contains("@bind-SelectedModelId=\"_selectedGenericModelId\"", Source, StringComparison.Ordinal);
        Assert.Contains("EditRequested=\"() => OpenImageEditor(img)\"", Source, StringComparison.Ordinal);
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
        // B-130: the identity step's identity IS IdentityReferences. It used to also hand the render the shared
        // reference list, which gave one face two identity channels in a single request - the duplicate path the
        // reference rules forbid. The step now says so explicitly.
        Assert.Contains("IdentityReferences = _productionIdentityFaceSelections", method, StringComparison.Ordinal);
        Assert.DoesNotContain("ReferenceApplications =", method, StringComparison.Ordinal);
        Assert.Contains("Image 1 is the existing scene and must remain the base image.", SceneImageServiceSource, StringComparison.Ordinal);
        Assert.Contains("additional approved face images are identity references only, not replacement images or composition sources.", SceneImageServiceSource, StringComparison.Ordinal);
        Assert.Contains("Treat the existing scene's visible neck and body skin tone as authoritative", SceneImageServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Select at least one approved identity pack", method, StringComparison.Ordinal);
        Assert.DoesNotContain("_productionIdentityPacks.Where(option => option.Selected)", method, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reference panels must offer the SELECTED model's real capabilities, and a model change must be answered.
    /// A list frozen per page hid every strategy it did not name - reported 2026-09-24: with Qwen-Image-2.1 selected
    /// "the identity is not available to allow but it should be", because the composer passed a text-only list and
    /// the studio's Finish stage a two-strategy list. Both surfaces now read the model choice's qualified set, which
    /// comes from the same capability decision the render makes, and both re-check what the operator had chosen.
    /// </summary>
    [Fact]
    public void ReferencePanels_FollowTheSelectedModelsCapabilities()
    {
        // B-130: both composition surfaces now present their step through the ONE composer, which reads the selected
        // model's qualified strategies itself (SelectedChoice.QualifiedStrategies) and offers each slot only what that
        // model can execute. A per-page strategy list is therefore not merely wrong, it must not exist - the assertions
        // below pin that absence, which is the stronger form of the guarantee this test was written for.
        Assert.Contains("ImageStepComposer Blueprint=\"CompositionBlueprint\"", CompositionComposerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecutableStrategies=", CompositionComposerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TextOnlyReferenceStrategy", CompositionComposerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecutableStrategies=\"ProductionModelStrategies\"", Source, StringComparison.Ordinal);
        Assert.Contains("SelectedChoice?.QualifiedStrategies", ComposerSource, StringComparison.Ordinal);

        // The studio's finish step now takes its references from the composer, which reads the selected model's
        // capability itself, so the page-level list it used to pass is gone. What must remain true is that the step is
        // declared from the cast the stage already resolves, and that a model change is answered.
        Assert.Contains("ProductionFinishBlueprint => ImageStepBlueprintFactory.ForEditElements(ProductionCast)", Source, StringComparison.Ordinal);
        Assert.Contains("SelectedModelIdChanged=\"OnProductionModelChanged\"", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("FinishReferenceStrategies = [\"TextOnly\", \"ReferenceConditioning\"]", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecutableStrategies=\"ProductionModelStrategies\"", Source, StringComparison.Ordinal);
        Assert.Contains("private IReadOnlyList<string> ProductionModelStrategies", Source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The identity card must say WHICH mechanism the selected model will use, re-check it whenever the picker
    /// changes, and refuse to submit an identity render a model cannot carry - a text-only render looks exactly like
    /// a successful identity render (reported 2026-09-24: "cant validate the changes made").
    /// </summary>
    [Fact]
    public void IdentityCard_ResolvesTheMechanismAndRefusesAnUncarryableRender()
    {
        Assert.Contains("@inject IReferenceStrategyResolver ReferenceStrategies", Source, StringComparison.Ordinal);
        Assert.Contains("Choices=\"_identityModelChoices\"", Source, StringComparison.Ordinal);
        Assert.Contains("@bind-SelectedModelId=\"_selectedIdentityModelId\"", Source, StringComparison.Ordinal);
        Assert.Contains("@bind-SelectedModelId:after=\"RefreshIdentityResolutionAsync\"", Source, StringComparison.Ordinal);
        Assert.Contains("_identityResolution = await ReferenceStrategies.ResolveIdentityAsync(_selectedIdentityModelId);", Source, StringComparison.Ordinal);
        Assert.Contains("@identityResolution.Strategy", Source, StringComparison.Ordinal);
        Assert.Contains("IdentityMechanismExplanation(identityResolution)", Source, StringComparison.Ordinal);
        Assert.Contains("_identityResolution?.IsAvailable != true", Source, StringComparison.Ordinal);
        Assert.Contains("The selected identity model cannot carry identity:", Source, StringComparison.Ordinal);
        // The stale "configure IP-Adapter/PuLid" guidance must name the native-reference route too.
        Assert.Contains("through its own reference images (Qwen-Image-2.1)", Source, StringComparison.Ordinal);
        // And a model change must refresh the derived capabilities, not only the picker's own value.
        Assert.Contains("Choices=\"_genericModelChoices\"", Source, StringComparison.Ordinal);
        Assert.Contains("@bind-SelectedModelId=\"_selectedGenericModelId\"", Source, StringComparison.Ordinal);
        Assert.Contains("@bind-SelectedModelId:after=\"OnGenericModelChangedAsync\"", Source, StringComparison.Ordinal);
        Assert.Contains("await RefreshIdentityResolutionAsync();\r\n        await RevalidateProductionReferenceStrategiesAsync();", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void AttemptExecutionDispositionAndApproval_AreRenderedSeparately()
    {
        Assert.Contains("<dt>Execution status</dt>", Source, StringComparison.Ordinal);
        Assert.Contains("<dt>Disposition</dt>", Source, StringComparison.Ordinal);
        Assert.Contains("<dt>Approval</dt>", Source, StringComparison.Ordinal);

        // The attempts render the SHARED card, so the three facts are kept apart by the projection that feeds it: an
        // execution status, a disposition badge, and an approval badge, each read from its own field. A projection that
        // folded two of them together is exactly how "shortlisted" would start reading as "approved".
        Assert.Contains("StatusText = img.Status.ToString(),", Source, StringComparison.Ordinal);
        Assert.Contains("DecisionBadgeText = img.Disposition is SceneImageAttemptDisposition.Shortlisted or SceneImageAttemptDisposition.Rejected", Source, StringComparison.Ordinal);
        Assert.Contains("ApprovalBadgeText = isApproved ? \"Production frame\" : null,", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionAttempts_ShowEveryStageAcrossTheFullWorkspaceWithAnEditorLink()
    {
        // Every list is derived in one place and every one of them renders the SAME card, so two lists cannot disagree
        // about what an attempt is.
        Assert.Contains("(\"Image edits\", _productionAttempts.Where(attempt => attempt.Operation == SceneImageOperation.Edit).ToList())", Source, StringComparison.Ordinal);
        Assert.Contains("(\"Composition\", AttemptsAtStage(SceneImageProductionStage.Composition))", Source, StringComparison.Ordinal);
        Assert.Contains("(\"Identity\", AttemptsAtStage(SceneImageProductionStage.Identity))", Source, StringComparison.Ordinal);
        Assert.Contains("(\"Finish\", AttemptsAtStage(SceneImageProductionStage.Finish))", Source, StringComparison.Ordinal);
        Assert.Contains("@foreach (var group in ProductionAttemptGroups())", Source, StringComparison.Ordinal);
        Assert.Contains("<ImageCard Model=\"CardModel(attempt, _selectedProductionAttemptId == attempt.Id)\"", Source, StringComparison.Ordinal);
        Assert.Contains("EditRequested=\"() => OpenProductionAttemptInEditor(attempt)\"", Source, StringComparison.Ordinal);
        Assert.Contains("/roleplay/image-editor/{sessionId}/{interactionId}/{attempt.Id}", Source, StringComparison.Ordinal);
        Assert.Contains(".scene-production-attempts {\r\n    grid-row: 6;", SceneImageStudioStylesheet, StringComparison.Ordinal);
        Assert.Contains(".scene-production-canvas-actions,\r\n.scene-production-attempts {\r\n    grid-column: 1 / -1;", SceneImageStudioStylesheet, StringComparison.Ordinal);
    }

    [Fact]
    public void SceneImageEditor_AutomaticallyAnalyzesSourceAndExposesNativeQwenReferences()
    {
        Assert.Contains("await _service.ReanalyzeAsync(_session.Id);", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.Contains("_descriptionPending = true;", SceneImageEditorSource, StringComparison.Ordinal);
        // The editor's model and reference slots come from the ONE composer, and its own strategy menu now comes from
        // the selected model's capability there. The pinned ["TextOnly","NativeMultiReference"] list this used to
        // assert is gone: it hid reference conditioning on every model that qualifies for it, and would have offered
        // native references on a model that has none. B-130 also removed the three UNADDRESSED element rows the page
        // rendered: the slots are the step's own, addressed per character, because a body reference must name whose.
        Assert.Contains("ImageStepComposer Blueprint=\"EditBlueprint\"", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.Contains("ShowPrompt=\"false\"", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EditorReferenceStrategies", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ReferenceApplyPanel", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.Contains("EditorModelId = request.EditorModelId", SceneImageEditAdapterSource, StringComparison.Ordinal);
        Assert.Contains("ReferenceApplications = request.ReferenceApplications.ToList()", SceneImageEditAdapterSource, StringComparison.Ordinal);
        Assert.Contains("SourceImageId = subject.ImageId", SceneImageEditAdapterSource, StringComparison.Ordinal);
        Assert.Contains("while (!string.IsNullOrWhiteSpace(root.SourceImageId)", SceneImageEditAdapterSource, StringComparison.Ordinal);
        Assert.Contains("image.Status == SceneImageStatus.Complete && !string.IsNullOrWhiteSpace(image.FileRelativePath)", SceneImageEditAdapterSource, StringComparison.Ordinal);
        Assert.Contains("class=\"scene-edit-lineage-thumb\"", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.Contains("Exact edit prompt", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.Contains("@image.PromptSnapshot", SceneImageEditorSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SceneImageEditor_RunsEditsInPlaceInsteadOfReturningToTheStudio()
    {
        Assert.DoesNotContain("Nav.NavigateTo($\"/roleplay/studio/{sessionId}/{interactionId}\"", SceneImageEditorSource, StringComparison.Ordinal);
        // The edit action's message names the stage it is actually in. It became "Edit compilation queued." when the
        // two-stage flow landed (prepare/compile first, then run): the workspace queues a COMPILATION here, not the
        // edit itself, and a status line that still said "Image edit queued." would describe a step that had not
        // started. What this test is about is unchanged - the action stays IN PLACE and keeps polling.
        Assert.Contains("_statusMessage = \"Edit compilation queued.\";", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.Contains("_statusMessage = \"Identity correction queued.\";", SceneImageEditorSource, StringComparison.Ordinal);
        // Both run actions must keep the workspace polling so the result renders in the in-place lineage.
        Assert.Matches("_statusMessage = \"Edit compilation queued\\.\";\\s*EnsurePolling\\(\\);", SceneImageEditorSource);
        Assert.Matches("_statusMessage = \"Identity correction queued\\.\";\\s*EnsurePolling\\(\\);", SceneImageEditorSource);
        // The tracked result stays visible while it progresses, and the session's newest image seeds the first render.
        Assert.Contains("_result = await _service.ResolveResultAsync(Subject, _session.Id, _result?.ImageId);", SceneImageEditorSource, StringComparison.Ordinal);
        Assert.Contains("if (!string.IsNullOrWhiteSpace(trackedResultId))", SceneImageEditAdapterSource, StringComparison.Ordinal);
        Assert.Contains("OrderByDescending(image => image.CreatedUtc)", SceneImageEditAdapterSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// B-137: the Krea 2 branch in Image Settings, and the scene-LoRA picker.
    ///
    /// <para>
    /// Krea 2 runs its own qualified sampler envelope (8 steps / cfg 1 / euler / simple) exactly like Qwen-Image-2.1,
    /// so the SDXL-family controls must be HIDDEN for it too - showing them as if they applied is the defect that
    /// produced the blown-out 2.1 render of 2026-09-24. The picker must be driven by the selected model's FAMILY
    /// (a LoRA only binds to the checkpoint family it was trained against) and must reach the page's settings object,
    /// or the operator's choice would be picked on screen and absent at render time.
    /// </para>
    /// </summary>
    [Fact]
    public void SceneImageStudio_Krea2HidesTheSdxlEnvelopeAndOffersTheSceneLoraPicker()
    {
        Assert.Contains(
            "SelectedGenericModelFamily is SceneImageModelFamily.QwenImage21 or SceneImageModelFamily.Krea2",
            Source,
            StringComparison.Ordinal);

        Assert.Contains("SceneLoraPicker Family=\"@SelectedGenericModelFamily\"", Source, StringComparison.Ordinal);
        Assert.Contains("Selections=\"_settings.SceneLoras\"", Source, StringComparison.Ordinal);
        Assert.Contains("SelectionsChanged=\"OnSceneLorasChanged\"", Source, StringComparison.Ordinal);
        Assert.Contains(
            "_settings.SceneLoras = selections.Count == 0 ? null : selections.ToList();",
            Source,
            StringComparison.Ordinal);

        // No negative-prompt control exists on this page for any family: B-135 purged it, and Krea 2's graph has no
        // input for one at all (its negative is a ConditioningZeroOut of the positive).
        Assert.DoesNotContain("NegativePrompt", Source, StringComparison.Ordinal);
    }

    /// <summary>
    /// B-137 §4 item 4c: the COMPOSITION render path carries scene LoRAs too.
    ///
    /// <para>
    /// It renders through the same <c>SceneImageStudioSettings</c> the studio does, so the render path resolves
    /// <c>SceneLoras</c> for it already - but with no control on the page the operator could never set one, and a
    /// composition could not apply an unlock or an act LoKr at all. This is a PRODUCTION surface, not a test one, so
    /// the gap was a real capability hole rather than a missing convenience.
    /// </para>
    /// </summary>
    [Fact]
    public void CompositionComposer_CarriesSceneLorasOnTheRender()
    {
        Assert.Contains(
            "SceneLoraPicker Family=\"@SelectedCompositionModelFamily\"",
            CompositionComposerSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "SelectionsChanged=\"OnSceneLorasChanged\"",
            CompositionComposerSource,
            StringComparison.Ordinal);

        // The selection has to reach the settings object the render is queued with, or it would be picked on screen
        // and absent from the render.
        Assert.Contains(
            "renderSettings.SceneLoras = _sceneLoras.ToList();",
            CompositionComposerSource,
            StringComparison.Ordinal);

        // The family filter is derived from the SELECTED model, never a constant.
        Assert.Contains(
            "?.Family ?? SceneImageModelFamily.Unknown;",
            CompositionComposerSource,
            StringComparison.Ordinal);

        // A scene LoRA binds to a model FAMILY, so changing the model must drop the selection rather than carry it to
        // a family it was not trained against.
        Assert.Contains("_sceneLoras = [];", CompositionComposerSource, StringComparison.Ordinal);
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
        // The gallery is now the shared ImageCard (the one /asset-studio card), so its actions are the card's own
        // icon buttons wired through callbacks rather than a private set of labelled text buttons.
        Assert.Contains("<ImageCard Model=\"CardModel(img)\"", SceneImageGallerySource, StringComparison.Ordinal);
        Assert.Contains("EditRequested=\"() => OpenEditor(img.InteractionId, img.Id)\"", SceneImageGallerySource, StringComparison.Ordinal);
        Assert.Contains("VideoRequested=\"() => OpenVideoComposer(img.InteractionId, img.Id)\"", SceneImageGallerySource, StringComparison.Ordinal);
        Assert.Contains("DeleteRequested=\"() => DeleteImageAsync(img)\"", SceneImageGallerySource, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"scene-gallery-actions\"", SceneImageGallerySource, StringComparison.Ordinal);
    }

    [Fact]
    public void EditIterateWorkbench_ForwardsBoundFieldsToItsParent()
    {
        // The boxes take committed text (CommittedTextInput) instead of @bind:get/@bind:set, which lost the caret; the
        // forwarding to the parent is unchanged, so assert that.
        Assert.Contains("LiveChanged=\"args => SetIntentAsync(args ?? string.Empty)\"", EditIterateWorkbenchSource, StringComparison.Ordinal);
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
        // The lineage marker is the card's own body line now, in words rather than a CSS class: which attempt an image
        // came from is a fact the operator reads, not a style hook.
        Assert.Contains("Root attempt", Source, StringComparison.Ordinal);
        Assert.Contains("From {parentId}", Source, StringComparison.Ordinal);
        Assert.Contains("BranchFromAttemptAsync", Source, StringComparison.Ordinal);
        Assert.Contains("Identity-stale", Source, StringComparison.Ordinal);
        Assert.Contains("CompareDifferences()", Source, StringComparison.Ordinal);
        Assert.Contains("IdentityReferenceBindingsJson", Source, StringComparison.Ordinal);
        Assert.Contains("Reference image {selected.Ordinal + 1}", SceneImageServiceSource, StringComparison.Ordinal);
        Assert.Contains("selected face identity reference for {binding.CharacterName} (CharacterFace:{binding.CharacterId})", SceneImageEditingJobHandlerSource, StringComparison.Ordinal);
        Assert.Contains("Attempt @", Source, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Attempt @(compareIndex == 0 ? \"A\" : \"B\")\"", Source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Approval states its reason and demands only what makes it EXACT. The stage ladder is deliberately NOT a
    /// precondition: it used to be written here, in the approval service and on the composition page, each with its own
    /// version, which is why the same attempt was approvable on one surface and not another.
    /// </summary>
    [Fact]
    public void ApprovalSurface_StatesGateReasonAndRequiresExactCompletedAttempt()
    {
        Assert.Contains("ApprovalReason(_selectedProductionAttempt)", Source, StringComparison.Ordinal);
        Assert.Contains("CanApprove(_selectedProductionAttempt)", Source, StringComparison.Ordinal);
        // One gate, in the domain, for every surface that offers the action.
        Assert.Contains("SceneImageFrameApprovalGate.BlockerFor(_productionGroup, attempt) is null", Source, StringComparison.Ordinal);
        Assert.Contains("SceneImageFrameApprovalGate.BlockerFor(_productionGroup, attempt) is { } blocker", Source, StringComparison.Ordinal);
        Assert.Contains("new decision version", Source, StringComparison.Ordinal);
        // Identity-staleness is stated as information rather than enforced as a blocker.
        Assert.Contains("approving it is still yours to decide", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("Identity required - this attempt has no completed identity pass.", Source, StringComparison.Ordinal);
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
        // The strip is filtered, so it iterates the filtered view of the same list rather than the raw list.
        var additionalLoop = IndexOf("@foreach (var img in VisibleAdditionalImages)", additionalImages);
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

    [Fact]
    public void ProductionStudio_LocationStageLeadsThePageLocalStageArrayAndOffersCreateBindAndSkip()
    {
        // B-148 D15: the page-local workbench stage array now leads with Location. The persisted SceneImageProductionStage
        // enum is untouched, so the keyboard array still holds Composition/Identity/Finish.
        Assert.Contains("new[] { \"Location\", \"Composition\", \"Identity\", \"Finish\" }", Source, StringComparison.Ordinal);
        Assert.Contains("Create location from this moment", Source, StringComparison.Ordinal);
        Assert.Contains("Skip — text-only location", Source, StringComparison.Ordinal);
        Assert.Contains("Finish in Location Studio", Source, StringComparison.Ordinal);
        Assert.Contains("Use as backdrop", Source, StringComparison.Ordinal);
        Assert.Contains("@card.Reason", Source, StringComparison.Ordinal);
        Assert.Contains("CreateLocationFromMomentAsync", Source, StringComparison.Ordinal);
        Assert.Contains("BindBackdropAsync", Source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Location stage is per-POV, and it says so: Omniscient and each character bind their own image, the moment's
    /// location link is keyed on that POV, and the stage resolves THAT POV's production group instead of binding onto
    /// whichever group happened to exist (which was a silent no-op when none did).
    /// </summary>
    [Fact]
    public void ProductionStudio_LocationStage_BindsOneImagePerPov()
    {
        Assert.Contains("this POV's own binding", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("shared across this moment's POVs", Source, StringComparison.Ordinal);

        // The stage resolves the POV's group through the ONE creation call the composer button uses, and a re-enrichment
        // restores this POV's recorded binding into that new group.
        Assert.Contains("EnsurePovProductionGroupAsync", Source, StringComparison.Ordinal);
        Assert.Contains("await LocationService.RestoreBackdropAsync(_productionGroup.Id)", Source, StringComparison.Ordinal);
        Assert.Contains("RequireProductionLineage();", Source, StringComparison.Ordinal);
        // The lineage guard exists once, so the stage and the composer button cannot disagree about "ready to produce".
        Assert.Equal(1, CountOccurrences(Source, "Exact completed Catalogue, Beat Production Plan, Moment Set, Moment enrichment, and production POV are required."));

        // A bind that cannot be recorded fails loudly; it must never return quietly and look like it worked.
        Assert.Contains("so its location binding has nowhere to be recorded", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (_productionGroup is null)\r\n            return;", Source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The stage's list is the MATCHED location's images, and only those. The rest of the library is reached through the
    /// full image search — which is the shared picker, in its bindable, searchable, groupable mode, and which binds
    /// through the same single path as the inline list.
    /// </summary>
    [Fact]
    public void ProductionStudio_LocationStage_ListsOnlyTheMatchedLocationAndSearchesTheRestThroughTheSharedPicker()
    {
        Assert.Contains("private SceneAsset? MatchedLocationContainer => _locationSuggestion?.Container;", Source, StringComparison.Ordinal);
        Assert.Contains("if (MatchedLocationContainer is not { } container)", Source, StringComparison.Ordinal);
        Assert.Contains("_locationImagesByAsset[container.Id] = (await AssetService.ListImagesAsync(container.Id)).ToList();", Source, StringComparison.Ordinal);
        // No "every Location asset and every image of each" load remains in the stage.
        Assert.DoesNotContain("asset.Type == SceneAssetType.Location", Source, StringComparison.Ordinal);

        Assert.Contains("Search all image assets", Source, StringComparison.Ordinal);
        Assert.Contains("OpenLocationAssetSearchAsync", Source, StringComparison.Ordinal);
        Assert.Contains("<InAppImagePicker Visible=\"_locationAssetSearchOpen\"", Source, StringComparison.Ordinal);
        Assert.Contains("AssetTypeFilter=\"SceneAssetType.Location\"", Source, StringComparison.Ordinal);
        Assert.Contains("RequireBindableImage=\"true\"", Source, StringComparison.Ordinal);
        Assert.Contains("BindPickedLocationAssetAsync", Source, StringComparison.Ordinal);
        // The modal binds by the SAME write the inline list uses, so there is one bind path rather than two.
        Assert.Contains("await BindBackdropAsync(identity.ImageId);", Source, StringComparison.Ordinal);

        // Whether an image can be bound is the domain's rule, so the list cannot offer one the bind would refuse.
        Assert.Contains("SceneImageLocationBindGate.BlockerFor(image)", Source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A bind is a write the operator asked for, so the stage reports its outcome. Rebinding the image already bound
    /// leaves the stage looking identical, so success has to be said out loud; and a bind that cannot be recorded shows
    /// its reason in the stage — never as an exception escaping the click handler, which left the operator with a
    /// Bind button that appeared to do nothing.
    /// </summary>
    [Fact]
    public void ProductionStudio_LocationStage_ReportsTheOutcomeOfEveryBind()
    {
        Assert.Contains("private string? _locationStageNotice;", Source, StringComparison.Ordinal);
        Assert.Contains("? $\"Bound '{bound.Label}' as this POV's location backdrop.\"", Source, StringComparison.Ordinal);
        Assert.Contains("<div class=\"alert alert-success py-2 small\" role=\"status\">@_locationStageNotice</div>", Source, StringComparison.Ordinal);

        // The group guard lives INSIDE the try, so a missing group is a message the stage shows rather than an
        // exception thrown out through the picker's Picked callback.
        var bindStart = Source.IndexOf("private async Task BindBackdropAsync(string imageId)", StringComparison.Ordinal);
        var bindEnd = Source.IndexOf("private Task OpenLocationAssetSearchAsync()", StringComparison.Ordinal);
        Assert.True(bindStart >= 0 && bindEnd > bindStart, "BindBackdropAsync could not be located in the studio source.");
        var bind = Source[bindStart..bindEnd];
        Assert.Contains("try\r\n        {\r\n            // No silent return", bind, StringComparison.Ordinal);
        Assert.Contains("?? throw new InvalidOperationException(\"This POV has no production group", bind, StringComparison.Ordinal);
        Assert.Contains("_locationStageError = exception.Message;", bind, StringComparison.Ordinal);
    }

    /// <summary>
    /// A binding the stage reports must show the IMAGE it names, not only its label. The stage's candidate list is the
    /// matched location's images, so a binding made from the full library (or a location that no longer matches) is in
    /// no list at all — the operator saw "Complete (bound: Indoor Back)" over an empty stage (reported 2026-10-19).
    /// </summary>
    [Fact]
    public void ProductionStudio_LocationStage_ShowsTheBoundImage()
    {
        Assert.Contains("Currently bound location backdrop", Source, StringComparison.Ordinal);
        Assert.Contains("_boundLocationImage is { FileRelativePath: not null }", Source, StringComparison.Ordinal);
        Assert.Contains("await AssetService.GetImageAsync(backdrop.ImageId)", Source, StringComparison.Ordinal);
        // A missing row or a missing file is said out loud; a blank box would look like a broken stage.
        Assert.Contains("no longer exists in the asset library", Source, StringComparison.Ordinal);
        Assert.Contains("has no stored file, so it cannot be displayed", Source, StringComparison.Ordinal);
        Assert.Contains("href=\"/locations/@boundBackdrop.AssetId\"", Source, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var index = haystack.IndexOf(needle, StringComparison.Ordinal); index >= 0;
             index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public void CompositionComposer_SeedsTheLocationSlotFromABoundBackdropAndShowsANotice()
    {
        Assert.Contains("SeedLocationBackdropIfBound", CompositionComposerSource, StringComparison.Ordinal);
        // The page asks the seed owner, which answers with the MODEL's capability; it must not build the binding
        // itself, because that is how a reference got attached to a model that cannot carry one (2026-10-13).
        Assert.Contains("LocationBackdropSlotPrefill.ResolveAsync(", CompositionComposerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LocationBackdropSlotPrefill.For(", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("This model cannot carry the location reference", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("_locationSeedNotice", CompositionComposerSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Measured 2026-10-14: a Composition draft's pre-processor call hit the DeepSeek provider's 360-second timeout,
    /// the prompt record went to Failed — and the panel showed "Prompt generation queued" and then nothing at all. The
    /// pending record was cleared on EVERY terminal status, so the banner that reads it could never render: the same
    /// load pass that discovered the failure nulled the field. The failure is kept now, and a reopened page reports
    /// the newest failed draft for its own group and brief.
    /// </summary>
    [Fact]
    public void CompositionComposer_SurfacesAFailedPromptGenerationInsteadOfDiscardingIt()
    {
        Assert.Contains("private SceneImagePromptRecord? _failedPrompt;", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("@_failedPrompt.ErrorMessage", CompositionComposerSource, StringComparison.Ordinal);
        // A terminal non-Complete generation is KEPT, not dropped.
        Assert.Contains("_failedPrompt = _pendingPrompt;", CompositionComposerSource, StringComparison.Ordinal);
        // A reopened page explains itself: the newest failed draft for this group and brief is read back.
        Assert.Contains("RefreshFailedPromptAsync", CompositionComposerSource, StringComparison.Ordinal);
        Assert.Contains("GetLatestPromptAsync(sessionId, interactionId)", CompositionComposerSource, StringComparison.Ordinal);
        // The banner must no longer be driven by the pending record, which is null by the time it could render.
        Assert.DoesNotContain(
            "_pendingPrompt?.Status == SceneImagePromptStatus.Failed", CompositionComposerSource, StringComparison.Ordinal);
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