namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The Composition Composer's image list is the production group's whole work, and every row says what it is and shows
/// what it was made with.
///
/// <para>
/// Measured 2026-10-16 on the operator's own production group 0f98cb6f: of 45 images, 29 were Composition-stage
/// attempts, 13 were image-editor edits recording NO stage at all, and 3 were preset edits. The page filtered on
/// <c>ProductionStage == Composition</c>, so 16 of 45 rows (36%) appeared nowhere on it — they were reachable only from
/// the Scene Image Studio's own "Image edits" tab. Operator report, verbatim: "needs to show both the composition and
/// edited images these should not be separated into separate things." Every one of those rows also recorded its model,
/// its reference set and its LoRAs, and none of it was shown: the page rendered a bare card while the Review Deck
/// already had a shared rail for exactly this. These contracts pin both fixes.
/// </para>
/// </summary>
public sealed class CompositionComposerProductionListContractTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string ComposerSource = File.ReadAllText(Path.Combine(
        Root, "DreamGenClone.Web", "Components", "Pages", "CompositionComposer.razor"));

    /// <summary>
    /// The list an operator reads is not the list the step reasons about. The stage filter is what "which Composition
    /// attempts exist" means and stays; the render list must be the same rows WITHOUT it, or an edit is invisible on
    /// the page whose card produced it.
    /// </summary>
    [Fact]
    public void TheListShowsTheGroupsWholeWorkNotOnlyItsCompositionStage()
    {
        Assert.Contains("_productionImages = groupImages", ComposerSource, StringComparison.Ordinal);
        Assert.Contains("@foreach (var attempt in _productionImages)", ComposerSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "@foreach (var attempt in _compositionAttempts)", ComposerSource, StringComparison.Ordinal);

        // The stage filter survives for the step itself — this is a second projection, not a replacement of the rule
        // that decides which attempts are Composition attempts.
        Assert.Contains(
            ".Where(attempt => attempt.ProductionStage == SceneImageProductionStage.Composition)",
            ComposerSource,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// One read for both projections. A second call would let the two lists describe two different instants of one
    /// group, which is how a page ends up showing a count its own rows disagree with.
    /// </summary>
    [Fact]
    public void TheGroupIsReadOnceForBothProjections()
    {
        Assert.Contains(
            "var groupImages = await ProductionService.ListAttemptsAsync(_productionGroup.Id);",
            ComposerSource,
            StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(ComposerSource, "ProductionService.ListAttemptsAsync("));
    }

    /// <summary>
    /// Each row says what it IS. Neither the stage nor the operation alone can say it: an editor edit records no stage,
    /// and a preset edit is an edit of an edit, so the label reads the operation and the stage together.
    /// </summary>
    [Fact]
    public void EachRowIsLabelledWithWhatItIs()
    {
        Assert.Contains("private static string KindLabel(SceneImageRecord attempt)", ComposerSource, StringComparison.Ordinal);
        Assert.Contains("@KindLabel(attempt)", ComposerSource, StringComparison.Ordinal);
        Assert.Contains("SceneImageProductionStage.Preset => \"Preset edit\"", ComposerSource, StringComparison.Ordinal);
        Assert.Contains("SceneImageProductionStage.MultiAngle => \"Camera edit\"", ComposerSource, StringComparison.Ordinal);
        Assert.Contains("_ => \"Edit\"", ComposerSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// A derived row names its parent. A flat list is only readable if an edit says what it was edited FROM, and an
    /// unnamed parent falls back to a short id rather than being shown as parentless.
    /// </summary>
    [Fact]
    public void ADerivedRowNamesTheImageItCameFrom()
    {
        Assert.Contains("private string? ParentLabel(SceneImageRecord attempt)", ComposerSource, StringComparison.Ordinal);
        Assert.Contains("from @parentLabel", ComposerSource, StringComparison.Ordinal);
        Assert.Contains("_productionImages.FirstOrDefault(", ComposerSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// The metadata is the SHARED rail and the SHARED projection — the same component and the same mapping the Review
    /// Deck renders for a scene image — rather than a second field list that would drift from it.
    /// </summary>
    [Fact]
    public void TheMetadataComesFromTheSharedRailAndTheReviewDeckMapping()
    {
        Assert.Contains(
            "<ImageMetadataRail Item=\"ReviewDeckMapper.MapStudio(attempt, _approvedAttemptIds.Contains(attempt.Id))\"",
            ComposerSource,
            StringComparison.Ordinal);

        // The tag store for a rendered scene image, which is the store this row's id belongs to.
        Assert.Contains("TagTarget=\"ImageTagTarget.Rendered\"", ComposerSource, StringComparison.Ordinal);
        Assert.Contains("TagsJson=\"attempt.TagsJson\"", ComposerSource, StringComparison.Ordinal);
        Assert.Contains(
            "Details=\"ImageGenerationInputDetails.FromSceneImage(attempt)\"", ComposerSource, StringComparison.Ordinal);
        Assert.Contains("TagsChanged=\"OnCompositionTagsChangedAsync\"", ComposerSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// A tag write updates the row in place instead of re-reading the group, the brief and the moment to learn one
    /// string — and both projections, so the two lists cannot disagree about one row's tags.
    /// </summary>
    [Fact]
    public void ATagWriteUpdatesBothProjectionsOfTheRow()
    {
        Assert.Contains("private Task OnCompositionTagsChangedAsync(IReadOnlyList<string> tags)", ComposerSource, StringComparison.Ordinal);
        Assert.Contains("list[index].TagsJson = json;", ComposerSource, StringComparison.Ordinal);
        Assert.Contains("foreach (var list in new[] { _productionImages, _compositionAttempts })", ComposerSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing is drawn that the page would then refuse. Regenerating means re-running the Composition, and
    /// <c>SceneImageService</c> refuses any source that is not a completed Composition attempt by name, so the command
    /// is offered on those rows only.
    /// </summary>
    [Fact]
    public void RegenerationIsOfferedOnlyWhereTheServiceAcceptsIt()
    {
        var gate = ComposerSource.IndexOf(
            "if (attempt.ProductionStage == SceneImageProductionStage.Composition)", StringComparison.Ordinal);
        var command = ComposerSource.IndexOf("RegenerateCompositionAsync(attempt)", StringComparison.Ordinal);

        Assert.True(gate >= 0, "The regeneration command must be gated on the row being a Composition.");
        Assert.True(command > gate, "The regeneration command must sit INSIDE that gate.");
    }

    /// <summary>
    /// Approval is offered only where the DOMAIN's gate allows it — the same call the approval itself makes — so the
    /// button cannot appear on a row whose write would be refused.
    /// </summary>
    [Fact]
    public void ApprovalIsOfferedOnlyWhereTheDomainGateAllowsIt()
    {
        Assert.Contains(
            "SceneImageFrameApprovalGate.BlockerFor(_productionGroup, attempt) is null", ComposerSource, StringComparison.Ordinal);
        Assert.Contains("AcceptRequested=\"@(CanApprove(attempt)", ComposerSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AcceptRequested=\"() => ApproveCompositionAttemptAsync(attempt)\"", ComposerSource, StringComparison.Ordinal);
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
