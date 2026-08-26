using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Infrastructure.Models;

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
}
