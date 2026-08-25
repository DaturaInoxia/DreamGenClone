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
}