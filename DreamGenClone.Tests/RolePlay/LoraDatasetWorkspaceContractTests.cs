using System.Text.RegularExpressions;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-123 Phase 1 surfaces, as source contracts. Two of these are the item's whole point: the workspace must be
/// a plan somebody works through cell by cell, so no batch action may exist in it, and identity reads must go
/// through the resolved character key rather than the route id (B-127).
/// </summary>
public sealed class LoraDatasetWorkspaceContractTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    private static string Workspace => Read("DreamGenClone.Web", "Components", "Editing", "LoraDatasetWorkspace.razor");

    [Fact]
    public void Studio_HostsTheWorkspaceOnTheLoRraImagesTab_AndNoLongerShowsAPlaceholder()
    {
        var source = Read("DreamGenClone.Web", "Components", "Pages", "CharacterStudio.razor");

        Assert.Contains("<LoraDatasetWorkspace CharacterId=\"@CharacterId\" />", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LoRA dataset coverage cells, created one at a time with pose + identity", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two panes: the plan on the left as the item being worked, the cell workspace on the right. The left pane
    /// is `col-lg-4` rather than an `xl` column because an `xl` one only becomes a pane above 1200px — below that the
    /// two halves stack and the cell list stops being a left pane at all (reported by the operator, 2026-09-25).
    /// </summary>
    [Fact]
    public void Workspace_IsTwoPanes_WithACollapsiblePlan()
    {
        var source = Workspace;

        Assert.Contains("col-lg-4", source, StringComparison.Ordinal);
        Assert.Contains("col-lg-8", source, StringComparison.Ordinal);
        Assert.Contains("col-lg-auto", source, StringComparison.Ordinal);
        Assert.DoesNotContain("col-xl-5", source, StringComparison.Ordinal);
        Assert.Contains("ToggleGrid", source, StringComparison.Ordinal);
        Assert.Contains("_gridCollapsed", source, StringComparison.Ordinal);
        Assert.Contains("Collapse cells", source, StringComparison.Ordinal);
        Assert.Contains("Show cells", source, StringComparison.Ordinal);
    }

    /// <summary>The cell workspace has to be able to shoot the cell: a model is chosen and one render is requested.</summary>
    [Fact]
    public void Workspace_HasAModelPickerAndARenderAction()
    {
        var source = Workspace;

        Assert.Contains("lora-cell-model", source, StringComparison.Ordinal);
        Assert.Contains("ModelResolutionService.ListSceneImageModelsAsync", source, StringComparison.Ordinal);
        Assert.Contains("CellService.ResolveCellModelAsync", source, StringComparison.Ordinal);
        Assert.Contains("CellService.SaveCellModelAsync", source, StringComparison.Ordinal);

        Assert.Contains("Render image", source, StringComparison.Ordinal);
        Assert.Contains("CellService.RenderCellAsync", source, StringComparison.Ordinal);
        Assert.Contains("CanRender", source, StringComparison.Ordinal);

        // The attempt deck: this cell's renders, each with its own discard.
        Assert.Contains("CellService.ListCellAttemptsAsync", source, StringComparison.Ordinal);
        Assert.Contains("CellService.DiscardAttemptAsync", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A render is one image for one cell. The service offers exactly one render method, it is singular, and nothing
    /// on the surface takes a collection of cells to shoot.
    /// </summary>
    [Fact]
    public void RenderSurface_OffersNoRangeAndSweepsNothing()
    {
        var renderMethods = typeof(ICharacterLoraCellService).GetMethods()
            .Where(member => member.Name.StartsWith("Render", StringComparison.Ordinal))
            .ToList();

        var render = Assert.Single(renderMethods);
        Assert.Equal("RenderCellAsync", render.Name);

        // Singular by signature: it takes a cell key, never a set of them.
        Assert.DoesNotContain(render.GetParameters(), parameter =>
            parameter.ParameterType != typeof(string)
            && parameter.ParameterType.IsGenericType
            && parameter.ParameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        var source = Workspace;
        Assert.DoesNotMatch(new Regex(@"foreach[^\n]*(_plan\.Records|Records)[^\n]*\n[^\n]*Render", RegexOptions.Multiline), source);
    }

    /// <summary>
    /// There is no way to shoot the dataset in one action. This is the rule the item exists to enforce: a
    /// "generate 30" button would produce near-duplicate images and a training set nobody judged.
    /// <para>
    /// The check is on the ACTION TARGETS rather than on the file's text, because the component explains in prose
    /// that there is no sweep — and an assertion that cannot tell an explanation from a button is not a contract.
    /// </para>
    /// </summary>
    [Fact]
    public void Workspace_HasNoBatchAction()
    {
        var handlers = Regex.Matches(Workspace, "@(?:onclick|onchange)=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToList();

        Assert.NotEmpty(handlers);
        foreach (var handler in handlers)
        {
            foreach (var forbidden in new[] { "All", "Batch", "Range", "Sweep", "Every", "Whole" })
            {
                Assert.DoesNotContain(forbidden, handler, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>And no loop that renders: every render in this pipeline is one operator action on one cell.</summary>
    [Fact]
    public void Workspace_ContainsNoRenderLoop()
    {
        var source = Workspace;

        Assert.DoesNotMatch(new Regex(@"foreach[^\n]*\n[^\n]*(Render|Generate|Dispatch)", RegexOptions.Multiline), source);
        Assert.DoesNotContain("ComfyUI", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every prompt is data. The component resolves templates and pastes the composed text; it must not carry a
    /// prompt sentence of its own, because that would be a wording decision with no UI to change it.
    /// </summary>
    [Fact]
    public void Workspace_ComposesPromptsFromTheStore_RatherThanCarryingItsOwn()
    {
        var source = Workspace;

        Assert.Contains("TemplateService.ResolveAsync", source, StringComparison.Ordinal);
        Assert.Contains("LoraCellPromptComposer.ComposeRenderPrompt", source, StringComparison.Ordinal);
        Assert.Contains("LoraCellPromptComposer.ComposeCaption", source, StringComparison.Ordinal);

        // No sentence longer than a label: the render prompt text lives in the seeded templates.
        foreach (var sentence in new[]
                 {
                     "Photorealistic", "Sharp focus", "natural skin texture", "no retouching", "negative prompt"
                 })
        {
            Assert.DoesNotContain(sentence, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>The readiness header reports the plan's own gaps, so the operator sees what is still missing.</summary>
    [Fact]
    public void Workspace_ShowsTheReadinessHeader_WithGaps()
    {
        var source = Workspace;

        Assert.Contains("cells accepted", source, StringComparison.Ordinal);
        Assert.Contains("captions", source, StringComparison.Ordinal);
        Assert.Contains("clothed / nude", source, StringComparison.Ordinal);
        Assert.Contains("Gaps", source, StringComparison.Ordinal);
        Assert.Contains("DescribeGaps", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// B-127: identity is keyed by the character template, so every identity read uses the resolved key. The
    /// route id may be a scenario character or an asset, and reading identity through it silently sees nothing.
    /// </summary>
    [Fact]
    public void Workspace_ResolvesTheOwner_AndNeverReadsIdentityThroughTheRouteId()
    {
        var source = Workspace;

        Assert.Contains("await OwnerResolver.ResolveAsync(CharacterId)", source, StringComparison.Ordinal);
        Assert.Contains("ListPacksAsync(_identityKey)", source, StringComparison.Ordinal);
        Assert.Contains("GetBodyCardAsync(_identityKey)", source, StringComparison.Ordinal);
        Assert.Contains("ListDatasetsAsync(_identityKey)", source, StringComparison.Ordinal);

        foreach (var forbidden in new[]
                 {
                     "ListPacksAsync(CharacterId)",
                     "GetBodyCardAsync(CharacterId)",
                     "ListDatasetsAsync(CharacterId)"
                 })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }
    }

    /// <summary>A component, not a page: the tab is the only way in, and it takes the character as a parameter.</summary>
    [Fact]
    public void Workspace_IsAComponentNotAPage()
    {
        var source = Workspace;

        Assert.DoesNotContain("@page", source, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public string? CharacterId", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The dataset is created from the generator and the plan is stored as the typed document — never assembled
    /// by hand in the UI, which is what the raw-JSON wireframe did.
    /// </summary>
    [Fact]
    public void Workspace_CreatesTheDatasetThroughTheGenerator_NotByTypingJson()
    {
        var source = Workspace;

        Assert.Contains("PlanGenerator.GenerateAsync", source, StringComparison.Ordinal);
        Assert.Contains("CoveragePlan.FromJson(dataset.CoveragePlanJson)", source, StringComparison.Ordinal);
        Assert.Contains("CurationPolicy.FromJson(dataset.CurationPolicyJson)", source, StringComparison.Ordinal);
        Assert.Contains("plan.ToJson()", source, StringComparison.Ordinal);

        // No JSON textarea: the coverage plan is not something an operator should ever hand-write.
        Assert.DoesNotContain("<textarea", source, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The face-only pack case has to be explained, not merely refused: the operator needs to know the body set
    /// comes first.
    /// </summary>
    [Fact]
    public void Workspace_ExplainsWhyABodyCompletePackIsRequired()
    {
        var source = Workspace;

        Assert.Contains("body-complete", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BodyComplete", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Body</strong> tab", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The attempt deck is the shared <c>CandidateGrid</c>, not a second one built here. Two decks in one app drift
    /// apart, and this one has no reason to differ: it shows images with a status and a selection.
    /// </summary>
    [Fact]
    public void Workspace_ReusesTheSharedCandidateGrid_ForTheAttemptDeck()
    {
        var source = Workspace;

        Assert.Contains("<CandidateGrid", source, StringComparison.Ordinal);
        Assert.Contains("CandidateGridItem", source, StringComparison.Ordinal);

        // No hand-rolled thumbnail grid: the only image tags are the component's own preview and the shared grid.
        var imgTags = Regex.Matches(source, "<img").Count;
        Assert.True(imgTags <= 1, $"expected no local attempt thumbnails, found {imgTags} <img> tags");
    }

    /// <summary>
    /// A cell is rendered AS the character. The service must go through the identity-aware generation path — the
    /// prompt-only path carries no conditioning at all — and it must resolve the reference from the cell's own rule
    /// rather than accept one from the caller.
    /// </summary>
    [Fact]
    public void CellRender_ConditionsOnIdentity_ThroughTheIdentityAwarePath()
    {
        var source = Read("DreamGenClone.Web", "Application", "RolePlay", "CharacterLoraCellService.cs");

        Assert.Contains("AddGeneratedImageAsync", source, StringComparison.Ordinal);
        Assert.Contains("Identity = conditioning", source, StringComparison.Ordinal);
        Assert.Contains("SceneAssetIdentityConditioning", source, StringComparison.Ordinal);

        // The prompt-only path cannot carry conditioning, so it must not be the one a cell render uses.
        Assert.DoesNotContain("CreateFromPromptAsync", source, StringComparison.Ordinal);

        // The reference comes from the stored plan and pack, never from a caller-supplied argument.
        Assert.Contains("ResolveIdentityConditioning(record, dataset.IdentityPackId, packAssets)", source, StringComparison.Ordinal);
        Assert.Contains("ListAssetsAsync(dataset.IdentityPackId", source, StringComparison.Ordinal);
    }

    /// <summary>And the workspace shows which reference the cell will be conditioned on, before it shoots.</summary>
    [Fact]
    public void Workspace_ShowsTheIdentityReferenceBeforeShooting()
    {
        var source = Workspace;

        Assert.Contains("CellService.DescribeIdentityReferenceAsync", source, StringComparison.Ordinal);
        Assert.Contains("_cellIdentityReference", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A cell is rendered on the character's BUILD as well as her face. The body reference must be resolved from the
    /// cell's own rule (its canonical slot and its wardrobe state) and carried in the same options object, because a
    /// mechanism that cannot take it refuses the render rather than dropping it silently.
    /// </summary>
    [Fact]
    public void CellRender_CarriesTheBodyReference_ResolvedFromTheCellsOwnState()
    {
        var source = Read("DreamGenClone.Web", "Application", "RolePlay", "CharacterLoraCellService.cs");

        Assert.Contains("BodyReference = bodyConditioning", source, StringComparison.Ordinal);
        Assert.Contains("SceneAssetBodyReferenceConditioning", source, StringComparison.Ordinal);
        Assert.Contains("ResolveBodyConditioning(record, dataset.IdentityPackId, packAssets)", source, StringComparison.Ordinal);

        // State and slot are BOTH matched: a clothed cell must never be handed the unclothed reference of the same
        // slot, because a reference image carries its clothing state into the render (verified 2026-09-23).
        Assert.Contains("asset.BodyView == record.BodyCanonicalSlot && asset.BodyState == record.BodyState", source, StringComparison.Ordinal);
    }

    /// <summary>And the workspace shows which body reference the cell will use, beside the face.</summary>
    [Fact]
    public void Workspace_ShowsTheBodyReferenceBeforeShooting()
    {
        var source = Workspace;

        Assert.Contains("CellService.DescribeBodyReferenceAsync", source, StringComparison.Ordinal);
        Assert.Contains("_cellBodyReference", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A cell render carries NO negative prompt. Every family this pipeline renders rejects one: SDXL / Juggernaut /
    /// BigLust resolve to an empty negative by model-author research, Pony takes only the short guard set its own
    /// compiler authors, and FLUX has no negative field at all. It would also be dead: the render path compiles a
    /// cell prompt (no compiler id is set) and authors no negative itself.
    /// </summary>
    [Fact]
    public void CellRender_CarriesNoNegativePrompt()
    {
        var service = Read("DreamGenClone.Web", "Application", "RolePlay", "CharacterLoraCellService.cs");
        var keys = Read("DreamGenClone.Domain", "RolePlay", "CharacterLoraCoverageWorkflowKeys.cs");
        var templates = Read("DreamGenClone.Infrastructure", "RolePlay", "ImageWorkflowRepository.cs");

        Assert.DoesNotContain("NegativePrompt", service, StringComparison.Ordinal);
        Assert.DoesNotContain("lora.cell.negative", keys, StringComparison.Ordinal);
        Assert.DoesNotContain("LoraCellWorkflowKeys.Negative", templates, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DreamGenClone.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
