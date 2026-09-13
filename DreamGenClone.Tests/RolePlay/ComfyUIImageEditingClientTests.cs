using System.Text.Json.Nodes;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Models;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ComfyUIImageEditingClientTests
{
    private static ResolvedImageEditorModel Resolve(ImageEditorGraphKind? graphKind = null) => new(
        ComfyUiUrl: "https://qwen.example.test",
        ProviderTimeoutSeconds: 120,
        ApiKeyEncrypted: null,
        ModelIdentifier: "qwen-image-edit-2511",
        ProviderName: "Qwen ComfyUI",
        ContentPolicy: ImageContentPolicy.AdultAllowed,
        DiffusionModel: "qwen_image_edit_2511_fp8mixed.safetensors",
        TextEncoder: "qwen_2.5_vl_7b_fp8_scaled.safetensors",
        Vae: "qwen_image_vae.safetensors",
        Steps: 40,
        Cfg: 4.0,
        Sampler: "euler",
        Scheduler: "simple",
        Denoise: 1.0,
        AuraFlowShift: 3.1,
        CfgNormStrength: 1.0,
        GraphKind: graphKind);

    [Fact]
    public void BuildResolvedWorkflow_SplitUnetGraph_EmitsSeparateLoaderGraph()
    {
        var workflow = ComfyUIImageEditingClient.BuildResolvedWorkflow(
            Resolve(ImageEditorGraphKind.SplitUnet),
            "input/source.png",
            "Move only the hand to the center of the shirt-covered chest.");
        var json = workflow.ToJsonString();

        Assert.Contains("UNETLoader", json, StringComparison.Ordinal);
        Assert.DoesNotContain("CheckpointLoaderSimple", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// A merged checkpoint (for example Qwen-Rapid-AIO-NSFW-v23 on a local ComfyUI host or the RunPod
    /// serverless endpoint) bundles model+clip+vae, so the graph must load it with
    /// <c>CheckpointLoaderSimple</c> and use that checkpoint's own sampler settings.
    /// </summary>
    [Fact]
    public void BuildResolvedWorkflow_MergedCheckpointGraph_EmitsCheckpointLoaderGraphWithResolvedSettings()
    {
        var model = Resolve(ImageEditorGraphKind.MergedCheckpoint) with
        {
            DiffusionModel = "Qwen-Rapid-AIO-NSFW-v23.safetensors",
            Steps = 8,
            Cfg = 1.0,
            Sampler = "euler_ancestral",
            Scheduler = "beta"
        };

        var workflow = ComfyUIImageEditingClient.BuildResolvedWorkflow(
            model,
            "input/source.png",
            "The man is now looking down.");
        var json = workflow.ToJsonString();
        var sampler = workflow["3"]!["inputs"]!.AsObject();

        Assert.Contains("CheckpointLoaderSimple", json, StringComparison.Ordinal);
        Assert.Contains("Qwen-Rapid-AIO-NSFW-v23.safetensors", json, StringComparison.Ordinal);
        Assert.DoesNotContain("UNETLoader", json, StringComparison.Ordinal);
        Assert.DoesNotContain("CLIPLoader", json, StringComparison.Ordinal);
        Assert.Equal(8, sampler["steps"]!.GetValue<int>());
        Assert.Equal(1.0, sampler["cfg"]!.GetValue<double>());
        Assert.Equal("euler_ancestral", sampler["sampler_name"]!.GetValue<string>());
        Assert.Equal("beta", sampler["scheduler"]!.GetValue<string>());
    }

    /// <summary>
    /// An editor LoRA affects BOTH the diffusion model and the CLIP, so the LoraLoader must feed the
    /// sampler branch AND both text encodes. Wiring only the model branch would silently apply the LoRA
    /// to the image path alone and leave the text conditioning unmodified.
    /// </summary>
    [Fact]
    public void BuildResolvedWorkflow_MergedCheckpointWithLora_RewiresModelAndBothClipBranches()
    {
        var model = Resolve(ImageEditorGraphKind.MergedCheckpoint) with
        {
            DiffusionModel = "Qwen-Rapid-AIO-NSFW-v23.safetensors",
            LoraName = "QwenEdit2511_AllIncludedGay_v2.safetensors",
            LoraStrength = 0.8
        };

        var workflow = ComfyUIImageEditingClient.BuildResolvedWorkflow(
            model,
            "input/source.png",
            "The man is now looking down.");

        var lora = workflow["17"]!.AsObject();
        Assert.Equal("LoraLoader", lora["class_type"]!.GetValue<string>());
        var loraInputs = lora["inputs"]!.AsObject();
        Assert.Equal("QwenEdit2511_AllIncludedGay_v2.safetensors", loraInputs["lora_name"]!.GetValue<string>());
        Assert.Equal(0.8, loraInputs["strength_model"]!.GetValue<double>());
        Assert.Equal(0.8, loraInputs["strength_clip"]!.GetValue<double>());
        Assert.Equal("16", loraInputs["model"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("16", loraInputs["clip"]!.AsArray()[0]!.GetValue<string>());

        Assert.Equal("17", workflow["5"]!["inputs"]!["model"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("17", workflow["6"]!["inputs"]!["clip"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("17", workflow["7"]!["inputs"]!["clip"]!.AsArray()[0]!.GetValue<string>());
    }

    [Fact]
    public void BuildResolvedWorkflow_SplitUnetWithLora_RewiresUnetAndClipLoader()
    {
        var model = Resolve(ImageEditorGraphKind.SplitUnet) with
        {
            LoraName = "QwenEdit2511_AllIncludedGay_v2.safetensors",
            LoraStrength = 1.0
        };

        var workflow = ComfyUIImageEditingClient.BuildResolvedWorkflow(
            model,
            "input/source.png",
            "The man is now looking down.");

        var loraInputs = workflow["17"]!["inputs"]!.AsObject();
        Assert.Equal("4", loraInputs["model"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("10", loraInputs["clip"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("17", workflow["5"]!["inputs"]!["model"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("17", workflow["6"]!["inputs"]!["clip"]!.AsArray()[0]!.GetValue<string>());
    }

    /// <summary>No configured LoRA must mean no node at all, never a defaulted weight.</summary>
    [Fact]
    public void BuildResolvedWorkflow_WithoutLora_EmitsNoLoraNode()
    {
        var workflow = ComfyUIImageEditingClient.BuildResolvedWorkflow(
            Resolve(ImageEditorGraphKind.MergedCheckpoint),
            "input/source.png",
            "Change only the shirt to red.");

        Assert.DoesNotContain("LoraLoader", workflow.ToJsonString(), StringComparison.Ordinal);
        Assert.Null(workflow["17"]);
    }

    /// <summary>A LoRA name with no strength must fail fast rather than inventing a weight.</summary>
    [Fact]
    public void BuildResolvedWorkflow_LoraNameWithoutStrength_FailsFast()
    {
        var model = Resolve(ImageEditorGraphKind.MergedCheckpoint) with
        {
            LoraName = "QwenEdit2511_AllIncludedGay_v2.safetensors"
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ComfyUIImageEditingClient.BuildResolvedWorkflow(
                model,
                "input/source.png",
                "Change only the shirt to red."));

        Assert.Contains("without a strength", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildResolvedWorkflow_UnconfiguredGraph_FailsFastWithoutGuessing()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ComfyUIImageEditingClient.BuildResolvedWorkflow(
                Resolve(),
                "input/source.png",
                "Change only the shirt to red."));

        Assert.Contains("Editor Graph", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GraphKindPersistence_RoundTripsKnownValuesAndRejectsUnknown()
    {
        Assert.Null(ImageEditorGraphKinds.ParseOrNull(null));
        Assert.Null(ImageEditorGraphKinds.ParseOrNull("  "));
        Assert.Equal(ImageEditorGraphKind.SplitUnet, ImageEditorGraphKinds.ParseOrNull("SplitUnet"));
        Assert.Equal(ImageEditorGraphKind.MergedCheckpoint, ImageEditorGraphKinds.ParseOrNull(" MergedCheckpoint "));
        Assert.Equal("SplitUnet", ImageEditorGraphKinds.ToPersistedValue(ImageEditorGraphKind.SplitUnet));
        Assert.Equal("MergedCheckpoint", ImageEditorGraphKinds.ToPersistedValue(ImageEditorGraphKind.MergedCheckpoint));
        Assert.Throws<InvalidOperationException>(() => ImageEditorGraphKinds.ParseOrNull("Split"));
    }

    [Fact]
    public void BuildWorkflow_UsesOnlyResolvedQwenArtifactsAndSamplerSettings()
    {
        var workflow = ComfyUIImageEditingClient.BuildWorkflow(
            Resolve(),
            "input/source.png",
            "Move only the hand to the center of the shirt-covered chest.");
        var json = workflow.ToJsonString();

        Assert.Contains("input/source.png", json, StringComparison.Ordinal);
        Assert.Contains("qwen_image_edit_2511_fp8mixed.safetensors", json, StringComparison.Ordinal);
        Assert.Contains("qwen_2.5_vl_7b_fp8_scaled.safetensors", json, StringComparison.Ordinal);
        Assert.Contains("qwen_image_vae.safetensors", json, StringComparison.Ordinal);
        Assert.Contains("\"steps\":40", json, StringComparison.Ordinal);
        Assert.Contains("\"cfg\":4", json, StringComparison.Ordinal);
        Assert.Contains("\"sampler_name\":\"euler\"", json, StringComparison.Ordinal);
        Assert.Contains("\"scheduler\":\"simple\"", json, StringComparison.Ordinal);
        Assert.Contains("\"denoise\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"shift\":3.1", json, StringComparison.Ordinal);
        Assert.Contains("\"strength\":1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("CheckpointLoaderSimple", json, StringComparison.Ordinal);
        Assert.DoesNotContain("CLIPSetLastLayer", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildWorkflow_BindsOrderedReferencesToBothEncoders(bool mergedCheckpoint)
    {
        var references = new[] { "becky-face.png", "dean-face.png" };
        var workflow = mergedCheckpoint
            ? ComfyUIImageEditingClient.BuildAioMergedCheckpointWorkflow(Resolve(), "composition.png", "Apply the approved faces.", references)
            : ComfyUIImageEditingClient.BuildWorkflow(Resolve(), "composition.png", "Apply the approved faces.", references);

        var positive = workflow["6"]!["inputs"]!.AsObject();
        var negative = workflow["7"]!["inputs"]!.AsObject();
        Assert.Equal(new JsonArray("20", 0).ToJsonString(), positive["image2"]!.ToJsonString());
        Assert.Equal(new JsonArray("21", 0).ToJsonString(), positive["image3"]!.ToJsonString());
        Assert.Equal(new JsonArray("20", 0).ToJsonString(), negative["image2"]!.ToJsonString());
        Assert.Equal(new JsonArray("21", 0).ToJsonString(), negative["image3"]!.ToJsonString());
        Assert.Equal("becky-face.png", workflow["20"]!["inputs"]!["image"]!.GetValue<string>());
        Assert.Equal("dean-face.png", workflow["21"]!["inputs"]!["image"]!.GetValue<string>());
        Assert.Equal(40, workflow["3"]!["inputs"]!["steps"]!.GetValue<int>());
        Assert.Equal(4, workflow["3"]!["inputs"]!["cfg"]!.GetValue<double>());
        Assert.Equal("euler", workflow["3"]!["inputs"]!["sampler_name"]!.GetValue<string>());
        Assert.Equal("simple", workflow["3"]!["inputs"]!["scheduler"]!.GetValue<string>());
        Assert.Equal(1, workflow["3"]!["inputs"]!["denoise"]!.GetValue<double>());
    }
}