using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Infrastructure.Models;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Pose + identity on ONE render (operator decision, 2026-09-22: "they should be able to run together").
///
/// The two conditionings compose because they touch DIFFERENT edges of the graph: the identity chain rewires the
/// sampler's <c>model</c> input (IP-Adapter / PuLID output), and the OpenPose ControlNet rewires its
/// <c>positive</c>/<c>negative</c> conditioning. These tests hold that separation, because the failure mode of getting
/// it wrong is silent — a graph where one conditioning overwrote the other would still render an image, just not the
/// one that was asked for.
/// </summary>
public sealed class ComfyUIIdentityWithPoseWorkflowTests
{
    private const string ControlNet = "thibaud-openpose-xl2/OpenPoseXL2.safetensors";

    private static IdentityControlledImageRequest Request() => new()
    {
        PositivePrompt = "Full-body photograph of a 40-year-old woman",
        NegativePrompt = "lowres, bad anatomy",
        Size = "1024x1536",
        CorrelationId = "render-1"
    };

    private static JsonObject IpAdapterGraph() => ComfyUIIdentityConditionedClient.BuildIpAdapterWorkflow(
        "juggernautXL_v9.safetensors", "PLUS FACE (portraits)", "identity-ref-render-1.png", Request(), 0.8);

    /// <summary>Without a pose, the graph is exactly the proven identity graph — nothing is added on spec.</summary>
    [Fact]
    public void WithoutAPose_TheIdentityGraphIsUnchanged()
    {
        var graph = IpAdapterGraph();

        var samplerInputs = graph["3"]!["inputs"]!.AsObject();
        Assert.Equal("12", samplerInputs["model"]![0]!.GetValue<string>());
        Assert.Equal("6", samplerInputs["positive"]![0]!.GetValue<string>());
        Assert.Equal("7", samplerInputs["negative"]![0]!.GetValue<string>());

        Assert.False(graph.ContainsKey("22"), "no ControlNet node may exist when no pose was asked for");
    }

    /// <summary>
    /// With a pose, BOTH conditionings are present and each drives the edge it belongs to. The identity half is
    /// untouched (the sampler's model is still the IP-Adapter output) and the ControlNet is inserted between the CLIP
    /// encodings and the sampler — not in place of either.
    /// </summary>
    [Fact]
    public void WithAPose_BothConditioningsArePresent_OnTheirOwnEdges()
    {
        var graph = IpAdapterGraph();

        ComfyUIIdentityConditionedClient.ApplyOpenPoseControlNet(graph, "pose-render-1.png", ControlNet, 0.8);

        // The identity half survives: the sampler's MODEL still comes from the IP-Adapter node.
        Assert.Equal("IPAdapter", graph["12"]!["class_type"]!.GetValue<string>());
        Assert.Equal("12", graph["3"]!["inputs"]!["model"]![0]!.GetValue<string>());

        // The pose half is inserted, and it feeds the CONDITIONING.
        Assert.Equal("LoadImage", graph["20"]!["class_type"]!.GetValue<string>());
        Assert.Equal("pose-render-1.png", graph["20"]!["inputs"]!["image"]!.GetValue<string>());
        Assert.Equal("ControlNetLoader", graph["21"]!["class_type"]!.GetValue<string>());
        Assert.Equal(ControlNet, graph["21"]!["inputs"]!["control_net_name"]!.GetValue<string>());
        Assert.Equal("ControlNetApplyAdvanced", graph["22"]!["class_type"]!.GetValue<string>());

        var samplerInputs = graph["3"]!["inputs"]!.AsObject();
        Assert.Equal("22", samplerInputs["positive"]![0]!.GetValue<string>());
        Assert.Equal(0, samplerInputs["positive"]![1]!.GetValue<int>());
        Assert.Equal("22", samplerInputs["negative"]![0]!.GetValue<string>());
        Assert.Equal(1, samplerInputs["negative"]![1]!.GetValue<int>());
    }

    /// <summary>
    /// The ControlNet conditions the CLIP ENCODINGS (6/7) — the sources the sampler read before the rewire. Getting
    /// this wrong (pointing it at itself, or at the sampler) is the difference between a graph that runs and one that
    /// silently drops the pose.
    /// </summary>
    [Fact]
    public void TheControlNet_ConditionsTheClipEncodings_NotItself()
    {
        var graph = IpAdapterGraph();

        ComfyUIIdentityConditionedClient.ApplyOpenPoseControlNet(graph, "pose-render-1.png", ControlNet, 0.8);

        var inputs = graph["22"]!["inputs"]!.AsObject();
        Assert.Equal("6", inputs["positive"]![0]!.GetValue<string>());
        Assert.Equal("7", inputs["negative"]![0]!.GetValue<string>());
        // ComfyUI 0.34 requires the VAE explicitly; it comes from the checkpoint loader.
        Assert.Equal("4", inputs["vae"]![0]!.GetValue<string>());
        Assert.Equal(2, inputs["vae"]![1]!.GetValue<int>());
    }

    /// <summary>The strength and the control net are the request's, not a constant written into the builder.</summary>
    [Fact]
    public void TheStrengthAndAdapter_ComeFromTheRequest()
    {
        var graph = IpAdapterGraph();

        ComfyUIIdentityConditionedClient.ApplyOpenPoseControlNet(graph, "pose.png", "other/ControlNet.safetensors", 0.55);

        Assert.Equal(0.55, graph["22"]!["inputs"]!["strength"]!.GetValue<double>());
        Assert.Equal("other/ControlNet.safetensors", graph["21"]!["inputs"]!["control_net_name"]!.GetValue<string>());
    }

    /// <summary>A graph with no sampler cannot be conditioned, and says so rather than producing a broken request.</summary>
    [Fact]
    public void AGraphWithNoSampler_IsRefused()
    {
        var graph = new JsonObject { ["4"] = new JsonObject { ["class_type"] = "CheckpointLoaderSimple" } };

        var error = Assert.Throws<InvalidOperationException>(
            () => ComfyUIIdentityConditionedClient.ApplyOpenPoseControlNet(graph, "pose.png", ControlNet, 0.8));

        Assert.Contains("no node '3'", error.Message, StringComparison.Ordinal);
    }

    /// <summary>PuLID shares the same tail, so the insert must compose with it too — not only with IP-Adapter.</summary>
    [Fact]
    public void TheInsert_AlsoComposesWithPuLid()
    {
        var graph = ComfyUIIdentityConditionedClient.BuildPuLidWorkflow(
            "juggernautXL_v9.safetensors", "pulid_v1.1.safetensors", "identity-ref-render-1.png", Request(), 0.9);

        ComfyUIIdentityConditionedClient.ApplyOpenPoseControlNet(graph, "pose.png", ControlNet, 0.8);

        Assert.Equal("ApplyPulid", graph["12"]!["class_type"]!.GetValue<string>());
        Assert.Equal("12", graph["3"]!["inputs"]!["model"]![0]!.GetValue<string>());
        Assert.Equal("22", graph["3"]!["inputs"]!["positive"]![0]!.GetValue<string>());
    }
}
