namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-135 — the rules adopted into the SDXL system prompts from the operator-supplied rewriter prompt, and the
/// clauses from that prompt that were REJECTED.
///
/// <para>
/// Assessment: <c>specs/Planning/B-135-image-playground/research/sdxl-rewriter-prompt-assessment.md</c>. The supplied
/// prompt was written for a TAG model wearing an SDXL label (its give-away clause was "never use underscores in any
/// tags"), so it was adopted selectively. This test pins both halves: the rules worth having must stay, and the
/// harmful ones must not come back — in particular the race-to-appearance inference, which is both factually wrong
/// and a violation of this repo's no-guessed-values rule.
/// </para>
///
/// <para>
/// A source-contract test rather than a behavioural one on purpose: it covers BOTH system prompts (the legacy and
/// the canonical) in one place, and it is the absence half that matters most — a behavioural test cannot easily
/// assert that a system prompt does NOT instruct the model to infer skin tone from nationality.
/// </para>
/// </summary>
public sealed class SdxlRewriterRulesTests
{
    /// <summary>
    /// Both files the SDXL instruction text lives in. B-135 moved the text itself into
    /// <c>SceneImageCompilerSystemPrompts</c> (so the per-checkpoint profile rows can be seeded from the same source the
    /// builder compiles with), while the builder keeps the provenance comments. Inspecting BOTH is STRICTER than
    /// inspecting one: a rejected clause now has to be absent from the shared prompts file as well, which is the file a
    /// profile row is seeded from and therefore the text that actually reaches a model.
    /// </summary>
    private static string CompilerSource() => string.Join(
        Environment.NewLine,
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "DreamGenClone.Web", "Application", "RolePlay", "SdxlSceneImagePromptBuilder.cs")),
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "DreamGenClone.Domain", "RolePlay", "SceneImageCompilerSystemPrompts.cs")));

    // ---- the adopted rules are present ---------------------------------------------------------------------

    [Theory]
    [InlineData("element order")]
    [InlineData("life stage")]
    [InlineData("masterpiece")]
    [InlineData("race, nationality or ethnicity")]
    [InlineData("same category")]
    [InlineData("resolution, aspect ratio or pixel count")]
    [InlineData("count and gender first")]
    public void BothSystemPromptsCarryTheAdoptedRules(string rule)
    {
        var source = CompilerSource();

        // Present once per prompt (legacy + canonical), so at least twice across the compiler text.
        var occurrences = CountOccurrences(source, rule, StringComparison.OrdinalIgnoreCase);

        Assert.True(occurrences >= 2,
            $"The adopted rule '{rule}' appears {occurrences} time(s) in the SDXL compiler text; it must be stated in "
            + "BOTH the legacy and the canonical SDXL system prompt.");
    }

    [Fact]
    public void TheMultiPersonCountAndGenderRuleIsStatedBeforeTheOtherTraits()
    {
        // Canon §2.6: without count + gender first, SDXL draws one person. The supplied prompt's element order omitted
        // this; ours must not.
        var source = CompilerSource();

        // Stated IDENTICALLY in both prompts, so the requirement cannot drift into one of them only.
        Assert.True(
            CountOccurrences(source, "count and gender first", StringComparison.OrdinalIgnoreCase) >= 2,
            "The count-and-gender rule must be stated in BOTH SDXL system prompts (canon §2.6: without it, SDXL draws one person).");
    }

    // ---- the rejected clauses are absent -------------------------------------------------------------------

    [Theory]
    [InlineData("dark black skin")]                  // the race -> appearance inference, by its own example
    [InlineData("older than middle-aged")]           // the age clamp
    [InlineData("teenager")]                         // an age word that can read as a minor in an adult pipeline
    [InlineData("triple backticks")]                 // a chat-UI fence; output goes to a DB record then a provider
    [InlineData("underscore")]                       // tag-model grammar (Danbooru), not SDXL natural language
    [InlineData("single-line, comma-separated")]     // tag-list grammar
    [InlineData("based on race or nationality")]     // the inference rule itself
    public void TheRejectedClausesAreNotInTheCompiler(string rejected)
    {
        var source = CompilerSource();

        Assert.False(
            source.Contains(rejected, StringComparison.OrdinalIgnoreCase),
            $"The rejected clause '{rejected}' is present in the SDXL compiler text. It was deliberately excluded "
            + "from the operator-supplied rewriter prompt when it was adopted — see the assessment record, and the "
            + "in-code comments at the point of adoption.");
    }

    [Fact]
    public void AppearanceIsStatedAsGiven_NotInferred()
    {
        var source = CompilerSource();

        // The corrected form of the rejected clause: the character record is authoritative and nothing is invented.
        Assert.Contains("never from inference", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("If a trait is absent, omit it rather than inventing one", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never derive skin tone, hair colour or hair style from a race", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheAdoptedRulesCarryTheirProvenanceInCode()
    {
        // Governance: a compiler change must be explainable and attributable. The comments name the source and the
        // assessment, so a later reader can tell author-researched rules from operator-supplied ones.
        var source = CompilerSource();

        Assert.Contains("operator-supplied SDXL rewriter prompt", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sdxl-rewriter-prompt-assessment.md", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REJECTED", source, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string haystack, string needle, StringComparison comparison)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, comparison)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DreamGenClone.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root (DreamGenClone.sln) not found above the test output directory.");
    }
}
