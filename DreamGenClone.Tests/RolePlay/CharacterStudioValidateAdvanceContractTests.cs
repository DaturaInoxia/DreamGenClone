namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The Validate gate reads the build's Validate STEP measurement, but the operator's "run validation" writes only the
/// IMAGE's result. Nothing joined the two, so a front chosen BEFORE it was validated — image verdict <c>Pass</c>,
/// step measurement absent — could never leave Validate:
///
/// <list type="bullet">
/// <item><b>Continue</b> checked the gate first and refused with "Run validation to continue" without ever taking the
/// measurement it was refusing for.</item>
/// <item><b>Re-validate</b> measured the image and then called <c>AdvanceAsync</c>, which recomputes the gate from the
/// still-empty step measurement and throws — "cannot advance past Validate: Run validation to continue", said to the
/// operator who had just run it.</item>
/// </list>
///
/// Reported 2026-10-09 on build <c>f7f91154…</c> (Dean): one front candidate, <c>Pass</c> at irisDy −1.29%, and no way
/// forward at all — the override control does not render for a passing verdict, and there was no second candidate to
/// select through the one path that did write the step measurement.
///
/// <para>
/// The invariant pinned here is the ORDER: take the step measurement, then judge and advance on what it says. Each
/// test reads the branch it is about and asserts that order, because a source assertion that merely finds both calls
/// somewhere in the method would pass in the exact broken arrangement this fixes.
/// </para>
/// </summary>
public sealed class CharacterStudioValidateAdvanceContractTests
{
    private static readonly string StudioSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Pages", "CharacterStudio.razor"));

    [Fact]
    public void ContinuePastValidate_TakesTheStepMeasurementBeforeJudgingTheGate()
    {
        var branch = Slice(
            "if (selectedAlready && _build.CurrentStep == CharacterIdentityBuildStep.Validate)",
            "await ValidationService.AdvanceAsync(_build.Id);");

        // The measurement the gate is computed from is taken HERE. Without this the branch can only ever refuse a
        // gate that has never been measured.
        var measure = branch.IndexOf("gate = await ValidationService.MeasureAsync(_build.Id);", StringComparison.Ordinal);
        var judge = branch.IndexOf("if (!gate.CanAdvance)", StringComparison.Ordinal);

        Assert.True(measure >= 0, "Continue does not take the Validate step's own measurement.");
        Assert.True(judge > measure, "Continue judges the gate before measuring it — the dead end is back.");

        // An explicit manual override is still never re-measured over: it is a human decision on the record.
        Assert.Contains("if (!gate.ManualOverrideApplied)", branch, StringComparison.Ordinal);
    }

    [Fact]
    public void Revalidate_MeasuresTheStepBeforeAdvancingOnTheImagesVerdict()
    {
        var body = Slice(
            "private async Task RevalidateAsync(SceneAssetImage attempt)",
            "private async Task RecordFrontOverrideAsync()");

        // MeasureImageAsync above wrote the IMAGE's result. The gate needs the STEP's, so it is taken before
        // advancing — and the advance is made conditional on what it says rather than hoping it passes.
        var measure = body.IndexOf("var gate = await ValidationService.MeasureAsync(_build.Id);", StringComparison.Ordinal);
        var advance = body.IndexOf("await ValidationService.AdvanceAsync(_build.Id);", StringComparison.Ordinal);

        Assert.True(measure >= 0, "Re-validate advances without taking the Validate step's measurement.");
        Assert.True(advance > measure, "Re-validate advances before measuring the step it advances past.");
        Assert.Contains("if (gate.CanAdvance)", body, StringComparison.Ordinal);

        // AdvanceAsync throws when the gate blocks, so an unguarded call here surfaces as a raw exception message.
        var gap = body[measure..advance];
        Assert.Contains("if (gate.CanAdvance)", gap, StringComparison.Ordinal);
    }

    /// <summary>The source from the start anchor through the first occurrence of the end anchor after it.</summary>
    private static string Slice(string startAnchor, string endAnchor)
    {
        var start = StudioSource.IndexOf(startAnchor, StringComparison.Ordinal);
        Assert.True(start >= 0, $"The source no longer contains '{startAnchor}'.");
        var end = StudioSource.IndexOf(endAnchor, start, StringComparison.Ordinal);
        Assert.True(end > start, $"The source no longer contains '{endAnchor}' after '{startAnchor}'.");

        return StudioSource[start..end];
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
