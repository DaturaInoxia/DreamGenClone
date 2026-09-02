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

    [Fact]
    public void ProductionCommands_AreProgressivelyGatedAndUseExactCompositionContract()
    {
        var studioStart = IndexOf("<div class=\"card mb-3 scene-production-studio\">");
        var createBranch = IndexOf("@if (IsSelectedMomentEnriched)", studioStart);
        var createCommand = IndexOf("@onclick=\"CreateOrLoadProductionAsync\"", createBranch);
        var productionBody = IndexOf("<div class=\"card-body\">", createCommand);
        Assert.True(createBranch < createCommand && createCommand < productionBody,
            "Create / Load Production must remain inside the enriched-Moment header branch.");
        Assert.Single(Regex.Matches(Source, "Create / Load Production", RegexOptions.CultureInvariant).Cast<Match>());

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
    public void Identity_IsSurfacedAsOnePassCompositionAndFinish_RemainsUnavailable()
    {
        // B-103 part A: identity is surfaced as a one-pass option inside the Composition stage (not
        // a separate Identity stage), and the Finish stage remains unavailable.
        Assert.Contains("Character Identity (one-pass)", Source, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"GenerateProductionCompositionWithIdentityAsync\"", Source, StringComparison.Ordinal);
        Assert.Contains("<strong>Finish</strong>", Source, StringComparison.Ordinal);
        Assert.Contains("Unavailable: finish-stage source-image editing follows the identity boundary", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerateProductionIdentityAsync", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerateProductionFinishAsync", Source, StringComparison.Ordinal);
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
    public void LegacyTools_ExcludeProductionAttemptsAndLabelIdentityTextToImageAsLegacy()
    {
        var productionStudio = IndexOf("scene-production-studio");
        var legacySection = IndexOf("Legacy / Experimental Tools", productionStudio);
        var legacyImages = IndexOf("<strong>Legacy Images</strong>", legacySection);
        var legacyLoop = IndexOf("@foreach (var img in _legacyImages)", legacyImages);
        Assert.True(productionStudio < legacySection && legacySection < legacyImages && legacyImages < legacyLoop,
            "Legacy tools and their image loop must remain after the production studio.");
        Assert.Contains("<strong>Legacy Identity-Conditioned Render</strong>", Source, StringComparison.Ordinal);
        Assert.Contains("private IEnumerable<SceneImageRecord> _legacyImages", Source, StringComparison.Ordinal);
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