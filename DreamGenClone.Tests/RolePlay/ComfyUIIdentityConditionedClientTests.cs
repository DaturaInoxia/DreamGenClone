using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Infrastructure.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Structure tests for the identity-conditioned ComfyUI workflows. These mirror the frozen proof
/// fixtures: IP-Adapter (PLUS FACE preset, weight 0.8) and PuLID (fidelity) single-actor graphs.
/// </summary>
public sealed class ComfyUIIdentityConditionedClientTests
{
    private static IdentityControlledImageRequest Request(long seed = 42) => new()
    {
        PositivePrompt = "Photorealistic shot of a single man, full body",
        NegativePrompt = "deformed, bad anatomy, extra limbs",
        Size = "1024x1024",
        Seed = seed,
        ReferenceImageBytes = [1, 2, 3],
        CorrelationId = "img-1"
    };

    [Fact]
    public void BuildIpAdapterWorkflow_UsesPinnedNodesPresetStrengthAndSeed()
    {
        var workflow = ComfyUIIdentityConditionedClient.BuildIpAdapterWorkflow(
            "juggernautXL_ragnarok.safetensors", "PLUS FACE (portraits)", "ref.png", Request(seed: 42), 0.8);

        Assert.Equal("CheckpointLoaderSimple", workflow["4"]!["class_type"]!.GetValue<string>());
        Assert.Equal("juggernautXL_ragnarok.safetensors", workflow["4"]!["inputs"]!["ckpt_name"]!.GetValue<string>());
        Assert.Equal("IPAdapterUnifiedLoader", workflow["10"]!["class_type"]!.GetValue<string>());
        Assert.Equal("PLUS FACE (portraits)", workflow["10"]!["inputs"]!["preset"]!.GetValue<string>());
        Assert.Equal("LoadImage", workflow["11"]!["class_type"]!.GetValue<string>());
        Assert.Equal("ref.png", workflow["11"]!["inputs"]!["image"]!.GetValue<string>());
        Assert.Equal("IPAdapter", workflow["12"]!["class_type"]!.GetValue<string>());
        Assert.Equal(0.8, workflow["12"]!["inputs"]!["weight"]!.GetValue<double>());
        Assert.Equal("standard", workflow["12"]!["inputs"]!["weight_type"]!.GetValue<string>());
        // Positive and negative prompts reach the two CLIPTextEncode nodes.
        Assert.Equal("Photorealistic shot of a single man, full body", workflow["6"]!["inputs"]!["text"]!.GetValue<string>());
        Assert.Equal("deformed, bad anatomy, extra limbs", workflow["7"]!["inputs"]!["text"]!.GetValue<string>());
        // KSampler is seeded from the request and wired from the IPAdapter node (12).
        Assert.Equal("KSampler", workflow["3"]!["class_type"]!.GetValue<string>());
        Assert.Equal(42, workflow["3"]!["inputs"]!["seed"]!.GetValue<long>());
        Assert.Equal(new JsonArray("12", 0).ToJsonString(), workflow["3"]!["inputs"]!["model"]!.ToJsonString());
        Assert.Equal("SaveImage", workflow["9"]!["class_type"]!.GetValue<string>());
    }

    [Fact]
    public void BuildPuLidWorkflow_UsesPuLidNodesAndFidelityMethod()
    {
        var workflow = ComfyUIIdentityConditionedClient.BuildPuLidWorkflow(
            "juggernautXL_ragnarok.safetensors", "pulid_model.safetensors", "ref.png", Request(seed: 7), 0.8);

        Assert.Equal("PulidModelLoader", workflow["10"]!["class_type"]!.GetValue<string>());
        Assert.Equal("pulid_model.safetensors", workflow["10"]!["inputs"]!["pulid_file"]!.GetValue<string>());
        Assert.Equal("PulidInsightFaceLoader", workflow["13"]!["class_type"]!.GetValue<string>());
        Assert.Equal("CPU", workflow["13"]!["inputs"]!["provider"]!.GetValue<string>());
        Assert.Equal("PulidEvaClipLoader", workflow["14"]!["class_type"]!.GetValue<string>());
        Assert.Equal("ApplyPulid", workflow["12"]!["class_type"]!.GetValue<string>());
        Assert.Equal("fidelity", workflow["12"]!["inputs"]!["method"]!.GetValue<string>());
        Assert.Equal(0.8, workflow["12"]!["inputs"]!["weight"]!.GetValue<double>());
        Assert.Equal("ref.png", workflow["11"]!["inputs"]!["image"]!.GetValue<string>());
        Assert.Equal(7, workflow["3"]!["inputs"]!["seed"]!.GetValue<long>());
        Assert.Equal(new JsonArray("12", 0).ToJsonString(), workflow["3"]!["inputs"]!["model"]!.ToJsonString());
    }

    [Fact]
    public void BuildMultiIpAdapterWorkflow_ChainsTwoRegionalIPAdapterNodes()
    {
        var request = Request(seed: 42);
        var references = new List<(string ReferenceName, string MaskName, IdentityReferenceInput Reference)>
        {
            ("dean.png", "dean_mask.png", new IdentityReferenceInput { CharacterLabel = "Dean", ReferenceImageBytes = [1], StrengthOverride = 0.8 }),
            ("becky.png", "becky_mask.png", new IdentityReferenceInput { CharacterLabel = "Becky", ReferenceImageBytes = [2] })
        };

        var workflow = ComfyUIIdentityConditionedClient.BuildMultiIpAdapterWorkflow(
            "juggernautXL_ragnarok.safetensors", "PLUS FACE (portraits)", references, request, defaultStrength: 0.6);

        // One LoadImage + LoadImageMask per character.
        Assert.Equal("LoadImage", workflow["11"]!["class_type"]!.GetValue<string>());
        Assert.Equal("dean.png", workflow["11"]!["inputs"]!["image"]!.GetValue<string>());
        Assert.Equal("LoadImage", workflow["13"]!["class_type"]!.GetValue<string>());
        Assert.Equal("becky.png", workflow["13"]!["inputs"]!["image"]!.GetValue<string>());

        Assert.Equal("LoadImageMask", workflow["12"]!["class_type"]!.GetValue<string>());
        Assert.Equal("dean_mask.png", workflow["12"]!["inputs"]!["image"]!.GetValue<string>());
        Assert.Equal("red", workflow["12"]!["inputs"]!["channel"]!.GetValue<string>());
        Assert.Equal("LoadImageMask", workflow["14"]!["class_type"]!.GetValue<string>());
        Assert.Equal("becky_mask.png", workflow["14"]!["inputs"]!["image"]!.GetValue<string>());

        // Two chained IPAdapter nodes, each with its own weight and regional mask.
        Assert.Equal("IPAdapter", workflow["20"]!["class_type"]!.GetValue<string>());
        Assert.Equal(0.8, workflow["20"]!["inputs"]!["weight"]!.GetValue<double>());
        Assert.Equal(new JsonArray("10", 0).ToJsonString(), workflow["20"]!["inputs"]!["model"]!.ToJsonString());
        Assert.Equal(new JsonArray("12", 0).ToJsonString(), workflow["20"]!["inputs"]!["attn_mask"]!.ToJsonString());
        Assert.Equal(new JsonArray("11", 0).ToJsonString(), workflow["20"]!["inputs"]!["image"]!.ToJsonString());

        Assert.Equal("IPAdapter", workflow["21"]!["class_type"]!.GetValue<string>());
        // Becky has no override, so the default strength applies.
        Assert.Equal(0.6, workflow["21"]!["inputs"]!["weight"]!.GetValue<double>());
        Assert.Equal(new JsonArray("20", 0).ToJsonString(), workflow["21"]!["inputs"]!["model"]!.ToJsonString());
        Assert.Equal(new JsonArray("14", 0).ToJsonString(), workflow["21"]!["inputs"]!["attn_mask"]!.ToJsonString());
        Assert.Equal(new JsonArray("13", 0).ToJsonString(), workflow["21"]!["inputs"]!["image"]!.ToJsonString());

        // KSampler is wired from the last chained IPAdapter node.
        Assert.Equal(new JsonArray("21", 0).ToJsonString(), workflow["3"]!["inputs"]!["model"]!.ToJsonString());
        Assert.Equal(42, workflow["3"]!["inputs"]!["seed"]!.GetValue<long>());
    }

    [Fact]
    public void BuildMultiIpAdapterWorkflow_ThrowsForSingleReference()
    {
        var references = new List<(string, string, IdentityReferenceInput)>
        {
            ("a.png", "a_mask.png", new IdentityReferenceInput())
        };

        Assert.Throws<ArgumentException>(() => ComfyUIIdentityConditionedClient.BuildMultiIpAdapterWorkflow(
            "ckpt", "preset", references, Request(), 0.6));
    }

    [Fact]
    public void SynthesizeBandMask_ProducesValidRegionPng()
    {
        var left = ComfyUIIdentityConditionedClient.SynthesizeBandMask(4, 4, 0, 2);
        using var leftImage = Image.Load<Rgba32>(left);
        Assert.Equal(4, leftImage.Width);
        Assert.Equal(4, leftImage.Height);
        // Character 0 is the left band: white on the left half, black on the right half.
        Assert.Equal(255, leftImage[0, 0].R);
        Assert.Equal(0, leftImage[3, 0].R);

        var right = ComfyUIIdentityConditionedClient.SynthesizeBandMask(4, 4, 1, 2);
        using var rightImage = Image.Load<Rgba32>(right);
        Assert.Equal(0, rightImage[0, 0].R);
        Assert.Equal(255, rightImage[3, 0].R);
    }
}
