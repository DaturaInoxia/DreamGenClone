using System.Text.Json.Nodes;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Models;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ComfyUIImageEditingClientTests
{
    private static ResolvedImageEditorModel Resolve() => new(
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
        CfgNormStrength: 1.0);

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