using System.Security.Cryptography;
using System.Text.Json;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-135 B135-039 — the vendored Qwen-Image-2.1 Prompt Enhancer prompts.
///
/// <para>
/// These two files are the vendor's own system prompts, and the answer contract they produce is part of what the
/// fine-tuned PE checkpoints were trained against. The only thing that makes them trustworthy is BYTE FIDELITY: a
/// re-typed, re-wrapped or "tidied" copy is a paraphrase that reviews as identical and quietly changes what the
/// model emits. So the assertions here are hashes and pinned provenance, not prose content.
/// </para>
///
/// <para>
/// The two prompts are NOT interchangeable and there is no merged prompt — asserted, because collapsing them into
/// one "Qwen prompt" is exactly the simplification that would break the contract.
/// </para>
/// </summary>
public sealed class QwenPromptEnhancerPromptAssetTests
{
    private const string AssetFolder = "helpers/qwen-prompt-enhancer/prompts";

    private static string AssetPath(string file) =>
        Path.Combine(FindRepositoryRoot(), "helpers", "qwen-prompt-enhancer", "prompts", file);

    private static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static JsonElement Manifest()
    {
        var path = AssetPath("manifest.json");
        Assert.True(File.Exists(path), $"The PE prompt manifest is missing: {path}");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    [Theory]
    [InlineData("system_prompt_t2i.txt")]
    [InlineData("system_prompt_edit.txt")]
    public void TheVendoredPromptMatchesItsRecordedHash(string file)
    {
        var path = AssetPath(file);
        Assert.True(File.Exists(path), $"The vendored PE system prompt is missing: {path}");

        var expected = Manifest().GetProperty("files")
            .EnumerateArray()
            .Single(entry => entry.GetProperty("file").GetString() == file)
            .GetProperty("sha256")
            .GetString();

        var actual = Sha256Of(path);

        Assert.True(
            string.Equals(expected, actual, StringComparison.Ordinal),
            $"{file} does not match its recorded hash — it has been edited, re-wrapped or replaced. "
            + $"Expected {expected}, got {actual}. Re-fetch with helpers/qwen-prompt-enhancer/fetch-official-prompts.ps1; "
            + "do NOT hand-edit a vendor prompt, because a paraphrase reviews as identical.");
    }

    [Fact]
    public void TheVendoredPromptsAreDifferentFiles()
    {
        // Two tasks, two checkpoints, two prompts. Collapsing them into one is the simplification that breaks the
        // answer contract, so assert they are genuinely distinct documents.
        var t2i = File.ReadAllText(AssetPath("system_prompt_t2i.txt"));
        var edit = File.ReadAllText(AssetPath("system_prompt_edit.txt"));

        Assert.NotEqual(t2i, edit);
        Assert.Contains("Image Prompt Rewriting Expert", t2i, StringComparison.Ordinal);
        Assert.Contains("Edit Prompt Enhancer", edit, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProvenanceIsPinnedToACommit_NotABranch()
    {
        var source = Manifest().GetProperty("source");
        var commit = source.GetProperty("commit").GetString()!;

        // A branch ref like "main" would let the vendored bytes drift out from under the recorded hashes.
        Assert.Equal(40, commit.Length);
        Assert.True(commit.All(Uri.IsHexDigit), $"Commit '{commit}' is not a hex SHA.");
        Assert.DoesNotContain("main", commit, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BothPromptsStateTheirAnswerContract()
    {
        // The contract is the part the weights were trained against; if it is absent the prompt is not the PE prompt.
        var t2i = File.ReadAllText(AssetPath("system_prompt_t2i.txt"));
        var edit = File.ReadAllText(AssetPath("system_prompt_edit.txt"));

        Assert.Contains("rewritten_prompt", t2i, StringComparison.Ordinal);
        Assert.Contains("wh_ratio", t2i, StringComparison.Ordinal);

        Assert.Contains("rewritten_prompt", edit, StringComparison.Ordinal);
        Assert.Contains("wh_ratio", edit, StringComparison.Ordinal);
        Assert.Contains("ratio_follow", edit, StringComparison.Ordinal);

        // ratio_follow is edit-only and the two ratio fields are mutually exclusive.
        Assert.DoesNotContain("ratio_follow", t2i, StringComparison.Ordinal);
        Assert.Contains("mutually exclusive", edit, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheT2iPromptStillForbidsQualityBoosters()
    {
        // Load-bearing and easy to "improve" away: the PE wants an observer's description, and a quality booster is
        // instruction, not observation. If someone re-adds one, this is the prompt that must not change.
        var t2i = File.ReadAllText(AssetPath("system_prompt_t2i.txt"));

        Assert.Contains("quality boosters", t2i, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("masterpiece", t2i, StringComparison.Ordinal);
        Assert.Contains("award-winning", t2i, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRecordedPresencePenaltyIsPerTaskAndNotNull()
    {
        // The vendor states the two values are not interchangeable and a wrong one "does not fail loudly — it quietly
        // changes the distribution you sample from". That is why it is recorded per task and must never be defaulted.
        var sampling = Manifest().GetProperty("samplingDefaults");

        var t2i = sampling.GetProperty("t2i").GetProperty("presencePenalty").GetDouble();
        var edit = sampling.GetProperty("edit").GetProperty("presencePenalty").GetDouble();

        Assert.Equal(1.5, t2i);
        Assert.Equal(0.0, edit);
        Assert.NotEqual(t2i, edit);
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
