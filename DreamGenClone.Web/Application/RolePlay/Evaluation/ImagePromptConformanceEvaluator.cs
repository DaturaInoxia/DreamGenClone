using System.Globalization;
using System.Text.RegularExpressions;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Evaluation;

/// <summary>How one check came out.</summary>
public enum ImagePromptCheckOutcome
{
    Pass = 0,

    Fail = 1,

    /// <summary>
    /// The check exists in the profile but CANNOT be decided mechanically — reported rather than silently passing, so
    /// the parts of a cell that are still eyeballed stay visible.
    /// </summary>
    Unverifiable = 2
}

public sealed record ImagePromptCheck(string Name, ImagePromptCheckOutcome Outcome, string Detail);

/// <summary>
/// The result of the FREE prompt layer (B-135 D20). No GPU, no model, no network: it inspects the compiled text.
/// </summary>
public sealed record ImagePromptConformanceResult(
    IReadOnlyList<ImagePromptCheck> Checks,
    double Similarity,
    double? RequiredTolerance)
{
    public bool HasFailures => Checks.Any(check => check.Outcome == ImagePromptCheckOutcome.Fail);

    public IReadOnlyList<ImagePromptCheck> Failures =>
        Checks.Where(check => check.Outcome == ImagePromptCheckOutcome.Fail).ToList();

    public IReadOnlyList<ImagePromptCheck> Unverifiable =>
        Checks.Where(check => check.Outcome == ImagePromptCheckOutcome.Unverifiable).ToList();

    /// <summary>
    /// A cell passes the prompt layer when nothing failed AND, where the cell declared a tolerance, the similarity
    /// reached it. A cell with NO declared tolerance cannot pass on similarity — an undeclared tolerance is a gap in
    /// the cell, not permission to skip the comparison.
    /// </summary>
    public bool Passed => !HasFailures && RequiredTolerance is { } tolerance && Similarity >= tolerance;
}

/// <summary>
/// Decides whether a compiled prompt is acceptable for a cell, using only what can be decided from the text itself
/// (B-135 B135-012).
///
/// <para>
/// Three kinds of check, and the distinction matters: rules that hold for EVERY checkpoint (no markdown fence, no
/// resolution, no quality boosters, no "no X" negation, one paragraph), rules that come from the checkpoint's DIALECT
/// (the Pony quality string must be there; Pony vocabulary must not leak into a natural-language prompt), and budgets
/// read from the checkpoint profile.
/// </para>
///
/// <para>
/// <b>What it deliberately does NOT do.</b> A profile's <c>RequiredComponentsJson</c> and <c>ForbiddenTokensJson</c>
/// hold CATEGORY names ("subject", "lighting", "pony-tag"), which are documentation, not patterns — no pure function
/// can decide "does this prompt name its lighting". Those are reported as
/// <see cref="ImagePromptCheckOutcome.Unverifiable"/> rather than passed by default, so a cell's eyeballed parts are
/// visible instead of implied. Making them decidable means giving the profile concrete patterns per checkpoint, which
/// is a data change, not a rule change.
/// </para>
/// </summary>
public static class ImagePromptConformanceEvaluator
{
    /// <summary>
    /// Quality/praise words. Banned by the canon and by the official Qwen Prompt Enhancer independently: they are
    /// instruction, not observation, and they do not change the render.
    /// </summary>
    private static readonly string[] QualityBoosters =
        ["masterpiece", "best quality", "high quality", "highly detailed", "ultra detailed", "8k", "4k", "award winning", "award-winning"];

    /// <summary>Pony/booru vocabulary that must never appear in a natural-language prompt.</summary>
    private static readonly string[] PonyVocabulary = ["score_", "rating_safe", "rating_questionable", "rating_explicit", "1girl", "1boy", "2people", "2girls", "2boys"];

    /// <summary>The researched Pony quality string's first tag; Pony's own author documents that the SHORT form is much weaker.</summary>
    private const string PonyQualityTag = "score_9";

    private static readonly Regex ResolutionPattern = new(
        @"\b\d{3,4}\s?[x×]\s?\d{3,4}\b|\b\d{1,2}:\d{1,2}\b|\b(4k|8k|1080p|720p)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NegationPattern = new(
        @"\b(no|not|without|never)\s+[a-z]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Word-ish tokens, lowercased. Deliberately naive and deterministic: no stemming, no stop-word list.</summary>
    public static IReadOnlyList<string> Tokenize(string text) =>
        text.Split([' ', '\t', '\r', '\n', ',', '.', ';', ':', '!', '?', '(', ')', '[', ']', '"', '\'', '/', '\\', '-', '_', '*'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.ToLowerInvariant())
            .Where(token => token.Length > 0)
            .ToList();

    /// <summary>
    /// Token-set Jaccard overlap, in [0,1]. Deterministic and explainable — NO embedding model, because an embedding
    /// would make a cell's verdict depend on weights nobody pinned (and the ONNX work was explicitly descoped, D7).
    /// </summary>
    public static double Similarity(string compiledPrompt, string expectedPrompt)
    {
        var compiled = Tokenize(compiledPrompt).ToHashSet(StringComparer.Ordinal);
        var expected = Tokenize(expectedPrompt).ToHashSet(StringComparer.Ordinal);

        if (compiled.Count == 0 && expected.Count == 0)
        {
            return 1.0;
        }

        if (compiled.Count == 0 || expected.Count == 0)
        {
            return 0.0;
        }

        var intersection = compiled.Intersect(expected, StringComparer.Ordinal).Count();
        var union = compiled.Union(expected, StringComparer.Ordinal).Count();
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    public static ImagePromptConformanceResult Evaluate(
        string? compiledPrompt,
        string? expectedPrompt,
        double? requiredTolerance,
        ImageCompilerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ImageCompilerProfileValidation.Validate(profile);

        var checks = new List<ImagePromptCheck>();
        var prompt = compiledPrompt ?? string.Empty;

        // ---- universal rules ------------------------------------------------------------------------------

        checks.Add(string.IsNullOrWhiteSpace(prompt)
            ? new ImagePromptCheck("non-empty", ImagePromptCheckOutcome.Fail, "The compiler returned an empty prompt.")
            : new ImagePromptCheck("non-empty", ImagePromptCheckOutcome.Pass, $"{prompt.Length} characters."));

        // One paragraph. A newline in a caption is a formatting leak into the provider request.
        checks.Add(prompt.Contains('\n') || prompt.Contains('\r')
            ? new ImagePromptCheck("single-paragraph", ImagePromptCheckOutcome.Fail, "The prompt contains a line break.")
            : new ImagePromptCheck("single-paragraph", ImagePromptCheckOutcome.Pass, "One line."));

        // No markdown fence. The compiler instructions forbid it; this catches a model that ignored them, so the fence
        // never reaches the audit record or the provider request.
        checks.Add(prompt.Contains("```", StringComparison.Ordinal) || prompt.Contains('`')
            ? new ImagePromptCheck("no-markdown-fence", ImagePromptCheckOutcome.Fail, "The prompt contains a backtick or code fence.")
            : new ImagePromptCheck("no-markdown-fence", ImagePromptCheckOutcome.Pass, "No fences."));

        // Resolution and aspect ratio travel in the image settings, never in the description (adopted rule).
        var resolutionMatch = ResolutionPattern.Match(prompt);
        checks.Add(resolutionMatch.Success
            ? new ImagePromptCheck("no-resolution-in-prompt", ImagePromptCheckOutcome.Fail,
                $"'{resolutionMatch.Value}' appears in the prompt; resolution and aspect ratio travel in the image settings.")
            : new ImagePromptCheck("no-resolution-in-prompt", ImagePromptCheckOutcome.Pass, "No resolution or ratio text."));

        var booster = QualityBoosters.FirstOrDefault(word => prompt.Contains(word, StringComparison.OrdinalIgnoreCase));
        checks.Add(booster is not null
            ? new ImagePromptCheck("no-quality-boosters", ImagePromptCheckOutcome.Fail,
                $"'{booster}' is a quality booster; style, medium and texture cues carry realism instead.")
            : new ImagePromptCheck("no-quality-boosters", ImagePromptCheckOutcome.Pass, "No quality boosters."));

        // "no X" is a negation dressed as a description. Under D10 there is no negative to move it into, so it is
        // simply not written.
        var negationMatch = NegationPattern.Match(prompt);
        checks.Add(negationMatch.Success
            ? new ImagePromptCheck("no-negation-phrasing", ImagePromptCheckOutcome.Fail,
                $"'{negationMatch.Value}' reads as a negation; state the desired state instead.")
            : new ImagePromptCheck("no-negation-phrasing", ImagePromptCheckOutcome.Pass, "No negations."));

        // ---- budget (from the profile) ---------------------------------------------------------------------

        checks.Add(prompt.Length < profile.MinChars
            ? new ImagePromptCheck("budget-characters", ImagePromptCheckOutcome.Fail,
                $"{prompt.Length} characters is below this checkpoint's minimum of {profile.MinChars}.")
            : prompt.Length > profile.MaxChars
                ? new ImagePromptCheck("budget-characters", ImagePromptCheckOutcome.Fail,
                    $"{prompt.Length} characters exceeds this checkpoint's limit of {profile.MaxChars}.")
                : new ImagePromptCheck("budget-characters", ImagePromptCheckOutcome.Pass,
                    $"{prompt.Length} characters within {profile.MinChars}-{profile.MaxChars}."));

        // Approximate on purpose: this counts whitespace/punctuation-delimited tokens, not the text encoder's subword
        // tokens. The character budget is the precise gate; this is a cheap proxy that catches gross overrun.
        var tokenCount = Tokenize(prompt).Count;
        checks.Add(tokenCount > profile.MaxTokens
            ? new ImagePromptCheck("budget-tokens", ImagePromptCheckOutcome.Fail,
                $"~{tokenCount} tokens exceeds this checkpoint's limit of {profile.MaxTokens} (approximate count).")
            : new ImagePromptCheck("budget-tokens", ImagePromptCheckOutcome.Pass, $"~{tokenCount} tokens of {profile.MaxTokens} (approximate)."));

        // ---- dialect (from the profile) --------------------------------------------------------------------

        if (profile.PromptDialect == SceneImagePromptDialect.PonyV6Tags)
        {
            // Pony's author documents the short form as much weaker, and the full 6-tag string as the training quirk
            // that matters, so its presence is a requirement rather than a preference.
            checks.Add(prompt.Contains(PonyQualityTag, StringComparison.OrdinalIgnoreCase)
                ? new ImagePromptCheck("dialect-required-tags", ImagePromptCheckOutcome.Pass, "Pony quality string present.")
                : new ImagePromptCheck("dialect-required-tags", ImagePromptCheckOutcome.Fail,
                    $"A Pony prompt must carry the quality string (expected '{PonyQualityTag}…')."));
        }
        else
        {
            // The exact defect reported 2026-09-24: Pony tags drafted into a natural-language prompt.
            var leaked = PonyVocabulary.FirstOrDefault(token => prompt.Contains(token, StringComparison.OrdinalIgnoreCase));
            checks.Add(leaked is not null
                ? new ImagePromptCheck("dialect-no-foreign-vocabulary", ImagePromptCheckOutcome.Fail,
                    $"Pony/booru token '{leaked}' appears in a {profile.PromptDialect} prompt.")
                : new ImagePromptCheck("dialect-no-foreign-vocabulary", ImagePromptCheckOutcome.Pass,
                    $"No Pony vocabulary in a {profile.PromptDialect} prompt."));
        }

        // ---- the similarity comparison (D12) ---------------------------------------------------------------

        var similarity = Similarity(prompt, expectedPrompt ?? string.Empty);
        checks.Add(requiredTolerance is { } tolerance
            ? new ImagePromptCheck("similarity-to-expected",
                similarity >= tolerance ? ImagePromptCheckOutcome.Pass : ImagePromptCheckOutcome.Fail,
                $"similarity {similarity.ToString("0.000", CultureInfo.InvariantCulture)} vs required {tolerance.ToString("0.###", CultureInfo.InvariantCulture)}.")
            : new ImagePromptCheck("similarity-to-expected", ImagePromptCheckOutcome.Fail,
                "This cell declares no similarity tolerance. A tolerance is never defaulted — declare one on the cell."));

        // ---- the honest gap --------------------------------------------------------------------------------

        checks.Add(new ImagePromptCheck("required-components", ImagePromptCheckOutcome.Unverifiable,
            $"The profile lists [{string.Join(", ", ReadNames(profile.RequiredComponentsJson))}] but these are category names, "
            + "not patterns, so no pure check can decide them. Verify by eye, or give the profile concrete patterns."));

        checks.Add(new ImagePromptCheck("forbidden-tokens", ImagePromptCheckOutcome.Unverifiable,
            $"The profile lists [{string.Join(", ", ReadNames(profile.ForbiddenTokensJson))}] as categories; only the "
            + "mechanically-checkable subset (dialect, negations, boosters, resolution) is enforced above."));

        return new ImagePromptConformanceResult(checks, similarity, requiredTolerance);
    }

    /// <summary>Reads the category names out of a profile's JSON array for reporting. Never used as a match pattern.</summary>
    private static IReadOnlyList<string> ReadNames(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return [];
            }

            return document.RootElement
                .EnumerateArray()
                .Where(element => element.ValueKind == System.Text.Json.JsonValueKind.String)
                .Select(element => element.GetString() ?? string.Empty)
                .Where(name => name.Length > 0)
                .ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }
}
