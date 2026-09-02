using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Infrastructure.Models;

/// <summary>
/// Routes source-image edits to the transport that matches the resolved editor provider's image
/// protocol: <see cref="ImageProtocol.ComfyUi"/> to the pod <see cref="ComfyUIImageEditingClient"/>,
/// <see cref="ImageProtocol.ComfyUiServerless"/> to the RunPod serverless
/// <see cref="RunPodServerlessEditingClient"/>. Only protocols accepted by the editor resolver are
/// handled; anything else fails fast (no silent fallback).
/// </summary>
public sealed class ImageEditingClientDispatcher : IImageEditingClient
{
    private readonly ComfyUIImageEditingClient _comfyUi;
    private readonly RunPodServerlessEditingClient _serverless;

    public ImageEditingClientDispatcher(
        ComfyUIImageEditingClient comfyUi,
        RunPodServerlessEditingClient serverless)
    {
        _comfyUi = comfyUi;
        _serverless = serverless;
    }

    public async Task<byte[]> EditAsync(
        ResolvedImageEditorModel model,
        Stream sourceImage,
        string sourceFileName,
        string instruction,
        CancellationToken cancellationToken = default)
    {
        return model.ImageProtocol switch
        {
            ImageProtocol.ComfyUi => await _comfyUi.EditAsync(model, sourceImage, sourceFileName, instruction, cancellationToken),
            ImageProtocol.ComfyUiServerless => await _serverless.EditAsync(model, sourceImage, sourceFileName, instruction, cancellationToken),
            _ => throw new ImageGenerationException(
                $"Image editor provider '{model.ProviderName}' does not support image editing over image protocol '{model.ImageProtocol}'. Configure a ComfyUI or RunPod Serverless editor in Model Manager (/model-manager).",
                model.ProviderName,
                reasonCode: "unsupported_editor_protocol")
        };
    }
}
