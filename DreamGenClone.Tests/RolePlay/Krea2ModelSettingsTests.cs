using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.ModelManager;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Krea 2's envelope is a MODEL property read from the model's own qualification, never a studio preference and
/// never a code default: an SDXL cfg (5) or an SDXL sampler on this cfg-1-distilled model is a blown-out render.
/// These tests pin the fail-fast contract that makes that impossible.
/// </summary>
public sealed class Krea2ModelSettingsTests
{
    private static RegisteredModel Model(string qualificationsJson) => new()
    {
        Id = "model-krea2",
        DisplayName = "Krea 2 Turbo (local ComfyUI)",
        ModelIdentifier = "krea2_turbo_fp8_scaled.safetensors",
        ModelKind = ModelKind.Image,
        SceneImageModelFamily = SceneImageModelFamily.Krea2,
        PromptDialect = SceneImagePromptDialect.Krea2NaturalLanguage,
        CapabilityQualificationsJson = qualificationsJson
    };

    private const string Qualified = """
        [{
          "Strategy": "TextToImage",
          "Qualified": true,
          "ProofId": "krea2-59-cell-matrix-20261001",
          "UnetName": "krea2_turbo_fp8_scaled.safetensors",
          "ClipName": "qwen3vl_4b_fp8_scaled.safetensors",
          "VaeName": "qwen_image_vae.safetensors",
          "Steps": 8,
          "Cfg": 1.0,
          "SamplerName": "euler",
          "Scheduler": "simple",
          "Denoise": 1.0
        }]
        """;

    [Fact]
    public void Resolve_QualifiedModel_ReadsEveryValueFromTheConfiguration()
    {
        var refs = Krea2ModelSettings.Resolve(Model(Qualified));

        Assert.Equal("krea2_turbo_fp8_scaled.safetensors", refs.UnetName);
        Assert.Equal("qwen3vl_4b_fp8_scaled.safetensors", refs.ClipName);
        Assert.Equal("qwen_image_vae.safetensors", refs.VaeName);
        Assert.Equal(8, refs.Steps);
        Assert.Equal(1.0, refs.Cfg);
        Assert.Equal("euler", refs.SamplerName);
        Assert.Equal("simple", refs.Scheduler);
        Assert.Equal(1.0, refs.Denoise);
    }

    [Fact]
    public void Resolve_NoQualification_FailsFastNamingTheStrategy()
    {
        var exception = Assert.Throws<ModelResolutionException>(() => Krea2ModelSettings.Resolve(Model("[]")));

        Assert.Contains("TextToImage", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Krea 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_UnqualifiedEntry_FailsFast()
    {
        var json = Qualified.Replace("\"Qualified\": true", "\"Qualified\": false", StringComparison.Ordinal);
        Assert.Throws<ModelResolutionException>(() => Krea2ModelSettings.Resolve(Model(json)));
    }

    [Theory]
    [InlineData("Steps")]
    [InlineData("Cfg")]
    [InlineData("Denoise")]
    [InlineData("UnetName")]
    [InlineData("ClipName")]
    [InlineData("VaeName")]
    [InlineData("SamplerName")]
    [InlineData("Scheduler")]
    public void Resolve_MissingRequiredValue_FailsFastNamingTheField(string field)
    {
        // Remove the field from the qualified entry and prove the resolver refuses rather than defaulting it.
        var json = System.Text.RegularExpressions.Regex.Replace(
            Qualified,
            $",\\s*\"{field}\":\\s*(\"[^\"]*\"|[0-9.]+)",
            string.Empty);
        Assert.DoesNotContain($"\"{field}\"", json, StringComparison.Ordinal);

        var exception = Assert.Throws<ModelResolutionException>(() => Krea2ModelSettings.Resolve(Model(json)));
        Assert.Contains(field, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_MalformedJson_FailsFast()
    {
        var exception = Assert.Throws<ModelResolutionException>(() => Krea2ModelSettings.Resolve(Model("{not json")));
        Assert.Contains("CapabilityQualificationsJson", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The LITERAL <c>CapabilityQualificationsJson</c> the seed writes into
    /// <c>dreamgenclone.dev.db</c> (both by the ad-hoc seed and by <c>dbq b137-krea2-configure</c>).
    ///
    /// <para>
    /// Pinning the exact stored string - rather than a hand-written lookalike - is the point: it closes the loop
    /// seed -> resolver -> graph, so a seed edit that drops or renames a field fails HERE instead of at the first
    /// render on a machine that seeded with the command.
    /// </para>
    /// </summary>
    private const string SeededQualificationJson = """
        [{"Strategy":"TextToImage","EndpointId":"80262aaa-b069-4be3-b6e2-8fb25c9f7520","Qualified":true,"ProofId":"krea2-59-cell-matrix-2026-10-01","UnetName":"krea2_turbo_fp8_scaled.safetensors","ClipName":"qwen3vl_4b_fp8_scaled.safetensors","VaeName":"qwen_image_vae.safetensors","Steps":8,"Cfg":1.0,"SamplerName":"euler","Scheduler":"simple","Denoise":1.0,"Note":"Text-to-image only: no reference conditioning, no edit path, no ControlNet. Krea-2 Turbo is cfg-1 distilled, so the graph zeroes the positive conditioning instead of taking a negative prompt - the sampler envelope is qualified here and is never read from the studio controls."}]
        """;

    [Fact]
    public void Resolve_TheSeededRow_ProducesTheVerifiedGraphInputs()
    {
        var refs = Krea2ModelSettings.Resolve(Model(SeededQualificationJson));

        Assert.Equal("krea2_turbo_fp8_scaled.safetensors", refs.UnetName);
        Assert.Equal("qwen3vl_4b_fp8_scaled.safetensors", refs.ClipName);
        Assert.Equal("qwen_image_vae.safetensors", refs.VaeName);
        Assert.Equal(8, refs.Steps);
        Assert.Equal(1.0, refs.Cfg);
        Assert.Equal("euler", refs.SamplerName);
        Assert.Equal("simple", refs.Scheduler);
        Assert.Equal(1.0, refs.Denoise);

        // ...and the graph built from the seeded row is the shape the 59-cell proof validated.
        var workflow = DreamGenClone.Infrastructure.Models.ComfyUIImageClient.BuildKrea2Workflow(
            refs.UnetName, refs, "a photograph", "1024x1024", seed: 1L);

        Assert.Equal("krea2", workflow["2"]!["inputs"]!["type"]!.GetValue<string>());
        Assert.Equal("ConditioningZeroOut", workflow["5"]!["class_type"]!.GetValue<string>());
        Assert.Equal(8, workflow["7"]!["inputs"]!["steps"]!.GetValue<int>());
        Assert.Equal(1.0, workflow["7"]!["inputs"]!["cfg"]!.GetValue<double>());
    }
}
