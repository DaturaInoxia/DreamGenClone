using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-135 manifest v2 — the contract between an agent-authored prompt manifest and a runnable suite.
///
/// <para>
/// The load-bearing test is <see cref="AnUnknownPropertyIsRefusedRatherThanDropped"/>. A manifest field named slightly
/// differently, or a variant key that no run will ever select, produces a cell that imports cleanly and carries no
/// prompt — and a run of it would look like a model failure rather than a typo. Everything here fails loudly for that
/// reason.
/// </para>
/// </summary>
public sealed class PromptSuiteManifestTests
{
    private const string Manifest = """
        {
          "suite": "baseline-positions",
          "purpose": "Model-agnostic sexual position catalog.",
          "models": [
            { "key": "biglust", "checkpoint": "bigLust_v16.safetensors", "kind": "Generate", "displayName": "BigLust" },
            { "key": "juggernaut", "checkpoint": "juggernautXL_ragnarok.safetensors", "kind": "Generate", "displayName": "Juggernaut" },
            { "key": "qwen-edit-2511", "checkpoint": "qwen_image_edit_2511.safetensors", "kind": "Edit", "displayName": "Qwen Edit 2511" }
          ],
          "positions": [ { "id": "cowgirl", "path": "positions/cowgirl.json" } ]
        }
        """;

    private const string Position = """
        {
          "id": "cowgirl",
          "title": "cowgirl",
          "actors": "1M1F",
          "closeup": false,
          "userInput": "Woman on top riding him",
          "expected": "a photorealistic couple in cowgirl position on a bed, warm lamp light, 35mm",
          "neutralScene": "Two naked adults on a bed, the woman straddling the man.",
          "negative": "extra limbs, fused legs",
          "variants": {
            "biglust": "Photorealistic explicit sex scene, one adult man and one adult woman, cowgirl position.",
            "juggernaut": "Photorealistic explicit sex scene, one adult man and one adult woman, cowgirl position, 35mm.",
            "qwen-edit-2511": "Change the scene so the woman straddles the man. Keep identity and lighting unchanged."
          },
          "settings": { "seed": 11223, "steps": 30, "cfg": 5.0, "width": 1024, "height": 1024 },
          "bindings": []
        }
        """;

    private static PromptSuiteManifest Parse(string? position = null, string? manifest = null) =>
        PromptSuiteManifestValidation.ParseManifest(
            manifest ?? Manifest,
            _ => position ?? Position);

    // ---- the happy path ---------------------------------------------------------------------------------

    [Fact]
    public void AWellFormedManifestImportsWithItsLegendAndPositions()
    {
        var parsed = Parse();

        Assert.Equal("baseline-positions", parsed.Suite);
        Assert.Equal(3, parsed.Models.Count);
        Assert.Single(parsed.Positions);

        var position = parsed.Positions[0];
        Assert.Equal("cowgirl", position.Id);
        Assert.Equal("1M1F", position.Actors);
        Assert.Equal("Woman on top riding him", position.UserInput);
        Assert.StartsWith("a photorealistic couple", position.Expected, StringComparison.Ordinal);
        Assert.Equal(3, position.Variants.Count);
        Assert.Contains("11223", position.SettingsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLegendSaysHowEachVariantIsApplied()
    {
        // Generate vs Edit is the distinction that decides whether a variant can be rendered on its own. An edit variant
        // needs a source image, so a run against it is a two-stage chain - not a text-to-image render.
        var parsed = Parse();

        Assert.Equal(PromptVariantKind.Generate, parsed.Model("biglust")!.Kind);
        Assert.Equal(PromptVariantKind.Edit, parsed.Model("qwen-edit-2511")!.Kind);
        Assert.Equal("bigLust_v16.safetensors", parsed.Model("biglust")!.Checkpoint);
        Assert.Null(parsed.Model("flux"));
    }

    [Fact]
    public void VariantKeysAreMatchedCaseInsensitively()
    {
        var parsed = Parse();

        Assert.Equal("juggernaut", parsed.Model("JUGGERNAUT")!.Key);
        Assert.Contains("cowgirl position, 35mm", parsed.Positions[0].RequireVariant("Juggernaut"), StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingVariantIsRefusedByName()
    {
        var parsed = Parse();

        var error = Assert.Throws<InvalidOperationException>(() => parsed.Positions[0].RequireVariant("flux"));

        Assert.Contains("cowgirl", error.Message, StringComparison.Ordinal);
        Assert.Contains("biglust", error.Message, StringComparison.Ordinal);
    }

    // ---- gaps are REPORTED, and nothing blocks a run ----------------------------------------------------

    [Fact]
    public void APositionMissingOneModelsVariantStillRunsForTheOthers()
    {
        // The whole point. A position without its 'flux' prompt must not stop biglust and juggernaut from running: the
        // gap is recorded against the cell, and the model it is missing is named so the run can skip it visibly.
        var broken = Position.Replace(
            "\"juggernaut\": \"Photorealistic explicit sex scene, one adult man and one adult woman, cowgirl position, 35mm.\",",
            string.Empty,
            StringComparison.Ordinal);

        var parsed = Parse(broken);
        var position = parsed.Positions[0];

        Assert.False(position.HasVariant("juggernaut"));
        Assert.True(position.HasVariant("biglust"));
        Assert.Contains(position.Problems, problem => problem.Contains("juggernaut", StringComparison.Ordinal));
        Assert.Contains(position.Problems, problem => problem.Contains("skipped", StringComparison.OrdinalIgnoreCase));

        // And the gap is not fatal at the suite level either.
        Assert.Empty(parsed.Problems);
        Assert.Equal("Photorealistic explicit sex scene, one adult man and one adult woman, cowgirl position.",
            position.RequireVariant("biglust"));
    }

    [Fact]
    public void AnUnknownPropertyIsReportedAndThePositionStillLoads()
    {
        // 'userPrompt' instead of 'userInput' is the typo that would otherwise vanish silently. It is now visible.
        var broken = Position.Replace("\"userInput\"", "\"userPrompt\"", StringComparison.Ordinal);

        var parsed = Parse(broken);
        var position = parsed.Positions[0];

        Assert.Contains(position.Problems, problem => problem.Contains("userPrompt", StringComparison.Ordinal));
        Assert.Contains(position.Problems, problem => problem.Contains("No 'userInput'", StringComparison.Ordinal));

        // Still runnable: the variants are intact, which is what a render actually needs.
        Assert.True(position.HasVariant("biglust"));
    }

    [Fact]
    public void AVariantKeyOutsideTheLegendIsReported()
    {
        var parsed = Parse(Position.Replace("\"biglust\":", "\"biglustt\":", StringComparison.Ordinal));
        var position = parsed.Positions[0];

        Assert.Contains(position.Problems, problem => problem.Contains("biglustt", StringComparison.Ordinal));
        Assert.True(position.HasVariant("biglustt"));
    }

    [Fact]
    public void AnEmptyVariantIsIgnoredAndReported()
    {
        var broken = Position.Replace(
            "\"biglust\": \"Photorealistic explicit sex scene, one adult man and one adult woman, cowgirl position.\"",
            "\"biglust\": \"   \"",
            StringComparison.Ordinal);

        var position = Parse(broken).Positions[0];

        Assert.False(position.HasVariant("biglust"));
        Assert.Contains(position.Problems, problem => problem.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AManifestWithNoModelLegendStillLoads()
    {
        // Without the legend a run cannot map a key to a checkpoint, so it says so. The suite still loads, and the key
        // can be picked by hand.
        var noLegend = Manifest.Replace("\"models\": [", "\"modellist\": [", StringComparison.Ordinal);

        var parsed = Parse(manifest: noLegend);

        Assert.Empty(parsed.Models);
        Assert.Single(parsed.Positions);
        Assert.True(parsed.Positions[0].HasVariant("biglust"));
        Assert.Contains(parsed.Problems, problem => problem.Contains("modellist", StringComparison.Ordinal));
        Assert.Contains(parsed.Problems, problem => problem.Contains("no 'models' legend", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ADuplicateModelKeyUsesTheFirstAndReports()
    {
        var duplicated = Manifest.Replace(
            "{ \"key\": \"juggernaut\", \"checkpoint\": \"juggernautXL_ragnarok.safetensors\", \"kind\": \"Generate\", \"displayName\": \"Juggernaut\" },",
            "{ \"key\": \"biglust\", \"checkpoint\": \"juggernautXL_ragnarok.safetensors\", \"kind\": \"Generate\", \"displayName\": \"Dup\" },",
            StringComparison.Ordinal);

        var parsed = Parse(manifest: duplicated);

        Assert.Equal(2, parsed.Models.Count);
        Assert.Contains(parsed.Problems, problem => problem.Contains("twice", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AnUnknownVariantKindIsReportedAndTreatedAsGenerate()
    {
        var broken = Manifest.Replace("\"kind\": \"Edit\"", "\"kind\": \"Transform\"", StringComparison.Ordinal);

        var parsed = Parse(manifest: broken);
        var edit = parsed.Model("qwen-edit-2511")!;

        Assert.Equal(PromptVariantKind.Generate, edit.Kind);
        Assert.Contains(parsed.Problems, problem => problem.Contains("Transform", StringComparison.Ordinal));
    }

    [Fact]
    public void AMissingPromptFieldIsReportedAndThePositionStillLoads()
    {
        // The fields a render or a verdict actually needs: losing one must be visible, but it is a gap in this position,
        // not a reason to refuse the manifest.
        foreach (var required in new[] { "userInput", "expected", "actors" })
        {
            // Indentation-tolerant: a raw string literal strips the common indent, so the runtime JSON is not indented
            // the way the source is.
            var broken = System.Text.RegularExpressions.Regex.Replace(
                Position, $"\n\\s*\"{required}\": \"[^\"]*\",", string.Empty);

            Assert.DoesNotContain($"\"{required}\":", broken, StringComparison.Ordinal);

            var position = Parse(broken).Positions[0];

            Assert.True(position.HasVariant("biglust"), $"Removing '{required}' must not make the position unrenderable.");
            Assert.Contains(position.Problems, problem => problem.Contains(required, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void ADescriptiveFieldIsNotRequiredAtAll()
    {
        // neutralScene and title are for the operator reading the catalog; the render needs neither, so their absence is
        // not even a problem. Reporting noise on every position would bury the gaps that matter.
        foreach (var optional in new[] { "neutralScene", "title", "closeup" })
        {
            var broken = System.Text.RegularExpressions.Regex.Replace(
                Position, $"\n\\s*\"{optional}\": [^\n]*,", string.Empty);

            var position = Parse(broken).Positions[0];

            Assert.True(position.HasVariant("biglust"));
            Assert.DoesNotContain(position.Problems, problem => problem.Contains(optional, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void AManifestWithNoPositionsLoadsAndReports()
    {
        var empty = Manifest.Replace(
            "\"positions\": [ { \"id\": \"cowgirl\", \"path\": \"positions/cowgirl.json\" } ]",
            "\"positions\": []",
            StringComparison.Ordinal);

        var parsed = Parse(manifest: empty);

        Assert.Empty(parsed.Positions);
        Assert.Contains(parsed.Problems, problem => problem.Contains("no positions", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AMalformedSettingsBlockIsIgnoredAndReported()
    {
        // Structurally valid JSON of the wrong type, so the reader has to decide rather than the parser failing.
        var broken = Position.Replace(
            "\"settings\": { \"seed\": 11223, \"steps\": 30, \"cfg\": 5.0, \"width\": 1024, \"height\": 1024 },",
            "\"settings\": [],",
            StringComparison.Ordinal);

        Assert.Contains("\"settings\": [],", broken, StringComparison.Ordinal);

        var position = Parse(broken).Positions[0];

        Assert.Equal("{}", position.SettingsJson);
        Assert.Contains(position.Problems, problem => problem.Contains("settings", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AMissingIdIsStillFatal()
    {
        // The one hard line: without an id a position cannot be tracked to its images, so there is nothing to run and
        // nothing to look at. This is the only field whose absence stops the file loading.
        var broken = System.Text.RegularExpressions.Regex.Replace(Position, "\n\\s*\"id\": \"[^\"]*\",", string.Empty);

        var error = Assert.Throws<InvalidOperationException>(() => Parse(broken));

        Assert.Contains("id", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyFileIsStillFatal()
    {
        Assert.Throws<InvalidOperationException>(() => Parse(string.Empty));
    }
}
