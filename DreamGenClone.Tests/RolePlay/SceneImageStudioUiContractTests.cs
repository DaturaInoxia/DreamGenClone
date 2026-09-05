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

    [Fact]
    public void ProductionCommands_AreProgressivelyGatedAndUseExactCompositionContract()
    {
        var studioStart = IndexOf("<div class=\"card mb-3 scene-production-studio\">");
        var createBranch = IndexOf("@if (IsSelectedMomentEnriched)", studioStart);
        var createCommand = IndexOf("@onclick=\"CreateOrLoadProductionAsync\"", createBranch);
        var productionBody = IndexOf("<div class=\"card-body\">", createCommand);
        Assert.True(createBranch < createCommand && createCommand < productionBody,
            "Create / Load Production must remain inside the enriched-Moment header branch.");
        Assert.Single(Regex.Matches(Source, "@onclick=\"CreateOrLoadProductionAsync\"", RegexOptions.CultureInvariant).Cast<Match>());

        var compositionCommand = IndexOf("@onclick=\"GenerateProductionCompositionAsync\"", productionBody);
        var compositionButtonStart = Source.LastIndexOf("<button", compositionCommand, StringComparison.Ordinal);
        var compositionButton = Source[compositionButtonStart..compositionCommand];
        Assert.Contains("_productionGroup is null", compositionButton, StringComparison.Ordinal);
        Assert.Contains("_compiledMediaBrief is null", compositionButton, StringComparison.Ordinal);
        Assert.Contains("_activePrompt?.Status != SceneImagePromptStatus.Complete", compositionButton, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(_editablePrompt)", compositionButton, StringComparison.Ordinal);

        var requestStart = IndexOf("new SceneRenderRequest", compositionCommand);
        var requestEnd = IndexOf("});", requestStart);
        var request = Source[requestStart..requestEnd];
        Assert.Contains("ProductionGroupId = _productionGroup.Id", request, StringComparison.Ordinal);
        Assert.Contains("CompiledMediaBriefId = _activePrompt.CompiledMediaBriefId", request, StringComparison.Ordinal);
        Assert.DoesNotContain("TypedReferenceSnapshotJson", request, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentGeneration_UsesDurableWorkspaceAndPreservesExistingGenerationSurfaces()
    {
        Assert.Contains("<ProductionWorkspace @key=\"_durableWorkspaceRefreshKey\" SessionId=\"@sessionId\" />", Source, StringComparison.Ordinal);
        Assert.Contains("SceneImageProductionSchema.CurrentGeneration", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("<div hidden=\"@IsCurrentProductionSession\">", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("scene-image-legacy-tools\" hidden=\"@IsCurrentProductionSession\"", Source, StringComparison.Ordinal);
        Assert.Contains("@if (IsSelectedMomentEnriched)", Source, StringComparison.Ordinal);
        Assert.Contains("@bind=\"_selectedProductionModelId\"", Source, StringComparison.Ordinal);
        Assert.Contains("@bind=\"_selectedGenericModelId\"", Source, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"() => OpenImageEditor(img)\"", Source, StringComparison.Ordinal);
        Assert.Contains("This session predates the current production schema. Create a new session", Source, StringComparison.Ordinal);
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
        Assert.Contains("@onclick=\"CancelSelectedAsync\"", ProductionWorkspaceSource, StringComparison.Ordinal);
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
    public void ApplyProductionIdentity_UsesResolvedReadinessInsteadOfHiddenPackSelection()
    {
        var methodStart = IndexOf("private async Task ApplyProductionIdentityAsync()");
        var methodEnd = IndexOf("private async Task SkipProductionIdentityAsync()", methodStart);
        var method = Source[methodStart..methodEnd];

        Assert.Contains("_productionIdentityReadiness.Select(binding => binding.CharacterName)", method, StringComparison.Ordinal);
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
    public void StagedWorkbench_ExposesReadinessParentSelectionAndSkipDialog()
    {
        Assert.Contains("StageState(SceneImageProductionStage.Composition)", Source, StringComparison.Ordinal);
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