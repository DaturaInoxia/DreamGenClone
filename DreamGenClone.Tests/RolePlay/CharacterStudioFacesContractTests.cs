namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Faces-section invariants of the Character Studio, all of them about a step never becoming a dead end.
///
/// B-121 note 008 — the Validate override control must be reachable on any candidate the eye gate blocks.
/// The control was rendered only inside the card that happened to be the build's *recorded* front, so a build
/// whose recorded front image had been deleted (no card matched it) showed no override control at all, while
/// the selection path printed "Record a manual override with a reason to continue." The UI could not do what
/// it told the user to do. ui-contract.md §3 states the override control is always visible.
///
/// The same note covers steps 3-5: they are steps, not requirements, so a front that needs no de-clothe, crop
/// or enhance must still have a way through to the face angles.
/// </summary>
public sealed class CharacterStudioFacesContractTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static readonly string CharacterStudioPath =
        Path.Combine(Root, "DreamGenClone.Web", "Components", "Pages", "CharacterStudio.razor");

    private static readonly string Source = File.ReadAllText(CharacterStudioPath);

    private static readonly string[] Lines = File.ReadAllLines(CharacterStudioPath);

    [Fact]
    public void FrontOverrideControl_IsNotGatedOnTheRecordedFront()
    {
        var condition = ConditionGuardingTheFrontOverrideControl();

        Assert.DoesNotContain("IsChosenFront", condition, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontOverrideControl_IsOfferedForBothBlockingVerdicts()
    {
        var condition = ConditionGuardingTheFrontOverrideControl();

        Assert.Contains("CharacterIdentityValidationVerdict.Fail", condition, StringComparison.Ordinal);
        Assert.Contains("CharacterIdentityValidationVerdict.NoFaceMesh", condition, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontOverrideControl_RecordsAgainstTheBuild()
    {
        Assert.Contains("RecordFrontOverrideAsync", Source, StringComparison.Ordinal);
        Assert.Contains("_frontOverrideReason", Source, StringComparison.Ordinal);
        Assert.Contains("_frontOverrideAuthor", Source, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordedFront_IsProtectedFromDeletion()
    {
        // The recorded front is what Validate measures; deleting it left the build naming a file that no
        // longer exists. The handler refuses it, and the card's delete control says so.
        Assert.Contains("IsChosenFront(image)", Source, StringComparison.Ordinal);
        Assert.Contains("recorded front cannot be deleted", Source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Steps 3-5 must not be a wall: Panel B has to offer the chosen front as-is, which records those three
    /// steps as skipped and moves the build to the face angles.
    /// </summary>
    [Fact]
    public void PanelB_OffersTheNoEditPathToTheFaceAngles()
    {
        Assert.Contains("ApproveFrontAsIsAsync", Source, StringComparison.Ordinal);
        Assert.Contains("skip 3 · 4 · 5", Source, StringComparison.Ordinal);

        // Offered only while no canonical front is approved: approving another one afterwards would rewrite
        // the recorded lineage of steps 3/4/5.
        Assert.Contains(
            "_garmentSource is not null && string.IsNullOrWhiteSpace(_build?.CanonicalFrontAssetId)",
            Source,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Every angle action must end in the one Panel C refresh. The card image list used to be read once, at
    /// page load, so an image created during the session could never appear on its card — the four uploaded
    /// angles then looked like a single image that the latest one kept replacing.
    /// </summary>
    [Fact]
    public void PanelC_EveryAngleActionEndsInTheOnePanelRefresh()
    {
        Assert.Contains("RefreshAnglePanelAsync", Source, StringComparison.Ordinal);

        // Exactly one place reads the attempts, and none of them reads a container to find a card's image.
        Assert.Equal(1, CountOccurrences(Source, "_angleAttempts = (await AnglesService.ListAttemptsAsync"));
        Assert.DoesNotContain("ListImagesAsync(canonical.AssetId)", Source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The angle gate's outcome must be readable on the card: the block reason it measured, the direction it
    /// recorded for a render that passed, and whether the render is the configured mirror remedy's product
    /// rather than the model's own output. Without these the gate is a silent failure again.
    /// </summary>
    [Fact]
    public void PanelC_ShowsTheGateVerdictTheMeasurementAndTheMirrorAttribution()
    {
        Assert.Contains("attempt.FailureReason", Source, StringComparison.Ordinal);
        Assert.Contains("angle?.FailureReason", Source, StringComparison.Ordinal);
        Assert.Contains("DescribeDirection(attempt)", Source, StringComparison.Ordinal);
        Assert.Contains("attempt.MirrorDerived", Source, StringComparison.Ordinal);
        Assert.Contains("Mirror-derived", Source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A profile's direction cannot be measured, so accepting one is the user's explicit confirmation — and the
    /// control that records it must be on the card whenever that confirmation is still outstanding. It used to
    /// render only while the view's status was <c>Pending</c>, which the upload path never sets, so the service's
    /// "requires explicit visual confirmation" refusal had nothing to tick.
    /// </summary>
    [Fact]
    public void PanelC_OffersTheProfileConfirmationWheneverItIsStillOutstanding()
    {
        var index = Source.IndexOf("I confirm this profile faces the expected image direction.", StringComparison.Ordinal);
        Assert.True(index > 0, "The profile confirmation control was not found in CharacterStudio.razor.");

        var block = Source[..index];
        var conditionStart = block.LastIndexOf("@if (", StringComparison.Ordinal);
        Assert.True(conditionStart >= 0, "The profile confirmation control has no @if condition above it.");

        var condition = block[conditionStart..];
        Assert.Contains("ManualConfirmationRequired", condition, StringComparison.Ordinal);
        Assert.Contains("CharacterIdentityAngleStatus.Accepted", condition, StringComparison.Ordinal);
        Assert.DoesNotContain("CharacterIdentityAngleStatus.Pending", condition, StringComparison.Ordinal);

        // It must NOT be narrowed further: a view can hold attempts before its record names an output (the upload
        // and render paths record the attempt first), and hiding the control while "Use this" still demands the
        // confirmation is a dead end (B-121 note 010).
        Assert.DoesNotContain("OutputArtifactId", condition, StringComparison.Ordinal);
    }

    /// <summary>
    /// Panel C shows who owns what: the attempts list is labelled with its view, and every card carries its own
    /// attempt count. The list only renders for the open card, so without this an image that had migrated between
    /// cards was indistinguishable from a legitimate one (B-121 note 010).
    /// </summary>
    [Fact]
    public void PanelC_LabelsOwnershipOfAttemptsAndCountsThemPerCard()
    {
        Assert.Contains("@AngleLabel(view) attempts (@_angleAttempts.Count)", Source, StringComparison.Ordinal);
        Assert.Contains("Attempts on this card: @AngleAttemptCount(view)", Source, StringComparison.Ordinal);
        Assert.Contains("_angleAttemptCounts[countedView] = (await AnglesService.ListAttemptsAsync(_build.Id, countedView)).Count", Source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Panel C's three per-view inputs must never be bound to a dictionary INDEXER. <c>@bind="_dict[key]"</c>
    /// reads the key during render, so a view or attempt the user had not touched yet threw
    /// <see cref="KeyNotFoundException"/> and killed the Blazor circuit — the Faces tab died with the
    /// "Reload" error UI as soon as a profile view needed confirming (B-121 note 009). The panel now reads
    /// through tolerant helpers and writes through explicit handlers.
    /// </summary>
    [Fact]
    public void PanelC_NeverBindsItsPerViewInputsToADictionaryIndexer()
    {
        // Comments are excluded: the helper's own doc comment quotes the offending syntax to explain the bug.
        Assert.DoesNotContain("@bind=\"_profileConfirmations[", Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("@bind=\"_overrideReasons[", Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("@bind=\"_overrideAuthors[", Markup, StringComparison.Ordinal);

        Assert.Contains("ProfileConfirmed(view)", Markup, StringComparison.Ordinal);
        Assert.Contains("SetProfileConfirmed(view", Markup, StringComparison.Ordinal);
        Assert.Contains("OverrideReason(attempt.Id)", Markup, StringComparison.Ordinal);
        Assert.Contains("OverrideAuthor(attempt.Id)", Markup, StringComparison.Ordinal);

        // And the readers really are tolerant: TryGetValue, not an indexer.
        Assert.Contains("_profileConfirmations.TryGetValue(view, out var confirmed)", Source, StringComparison.Ordinal);
        Assert.Contains("_overrideReasons.TryGetValue(attemptId, out var reason)", Source, StringComparison.Ordinal);
        Assert.Contains("_overrideAuthors.TryGetValue(attemptId, out var author)", Source, StringComparison.Ordinal);
    }

    /// <summary>The component's markup and code with line comments removed, for assertions about what it does.</summary>
    private static readonly string Markup = string.Join(
        '\n',
        Lines.Where(line =>
        {
            var trimmed = line.TrimStart();
            return !trimmed.StartsWith("///", StringComparison.Ordinal)
                && !trimmed.StartsWith("//", StringComparison.Ordinal)
                && !trimmed.StartsWith("@*", StringComparison.Ordinal);
        }));

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// The <c>@if</c> condition that guards the front override inputs, assembled from the lines between that
    /// condition and the bound reason input.
    /// </summary>
    private static string ConditionGuardingTheFrontOverrideControl()
    {
        var input = Array.FindIndex(
            Lines, line => line.Contains("@bind=\"_frontOverrideReason\"", StringComparison.Ordinal));
        Assert.True(input > 0, "The front override reason input was not found in CharacterStudio.razor.");

        var start = -1;
        for (var i = input - 1; i >= 0; i--)
        {
            if (Lines[i].TrimStart().StartsWith("@if (", StringComparison.Ordinal))
            {
                start = i;
                break;
            }
        }

        Assert.True(start >= 0, "The front override block has no @if condition above it.");

        var condition = new List<string>();
        for (var i = start; i < input; i++)
        {
            var line = Lines[i].Trim();
            condition.Add(line);
            if (line.EndsWith('{'))
            {
                break;
            }
        }

        return string.Join(' ', condition);
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
