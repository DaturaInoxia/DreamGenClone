using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.Models;

/// <summary>ComfyUI client for the persisted Qwen source-image editing workflow.</summary>
public sealed class ComfyUIImageEditingClient : IImageEditingClient
{
    /// <summary>Failure-code prefix for this client; the shared transport composes its codes from it.</summary>
    private const string EditReasonPrefix = "comfyui_edit";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApiKeyEncryptionService _encryptionService;
    private readonly ILogger<ComfyUIImageEditingClient> _logger;

    public ComfyUIImageEditingClient(
        IHttpClientFactory httpClientFactory,
        IApiKeyEncryptionService encryptionService,
        ILogger<ComfyUIImageEditingClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    internal static JsonObject BuildWorkflow(
        ResolvedImageEditorModel model,
        string sourceImageName,
        string instruction,
        IReadOnlyList<string>? referenceImageNames = null)
    {
        var references = referenceImageNames ?? [];
        var positiveInputs = new JsonObject
        {
            ["clip"] = new JsonArray("10", 0),
            ["vae"] = new JsonArray("11", 0),
            ["image1"] = new JsonArray("2", 0),
            ["prompt"] = instruction
        };
        var negativeInputs = new JsonObject
        {
            ["clip"] = new JsonArray("10", 0),
            ["vae"] = new JsonArray("11", 0),
            ["image1"] = new JsonArray("2", 0),
            ["prompt"] = string.Empty
        };
        AddReferenceInputs(positiveInputs, negativeInputs, references);

        // Hoisted so the optional editor LoRA below can re-point the sampler branch at the LoraLoader.
        var auraFlowInputs = new JsonObject
        {
            ["model"] = new JsonArray("4", 0),
            ["shift"] = model.AuraFlowShift
        };

        var workflow = new JsonObject
        {
            ["1"] = new JsonObject
            {
                ["class_type"] = "LoadImage",
                ["inputs"] = new JsonObject { ["image"] = sourceImageName }
            },
            ["2"] = new JsonObject
            {
                ["class_type"] = "FluxKontextImageScale",
                ["inputs"] = new JsonObject { ["image"] = new JsonArray("1", 0) }
            },
            ["3"] = new JsonObject
            {
                ["class_type"] = "KSampler",
                ["inputs"] = new JsonObject
                {
                    ["model"] = new JsonArray("14", 0),
                    ["positive"] = new JsonArray("12", 0),
                    ["negative"] = new JsonArray("13", 0),
                    ["latent_image"] = new JsonArray("8", 0),
                    ["seed"] = Random.Shared.NextInt64(long.MaxValue),
                    ["steps"] = model.Steps,
                    ["cfg"] = model.Cfg,
                    ["sampler_name"] = model.Sampler,
                    ["scheduler"] = model.Scheduler,
                    ["denoise"] = model.Denoise
                }
            },
            ["4"] = new JsonObject
            {
                ["class_type"] = "UNETLoader",
                ["inputs"] = new JsonObject { ["unet_name"] = model.DiffusionModel, ["weight_dtype"] = "default" }
            },
            ["5"] = new JsonObject
            {
                ["class_type"] = "ModelSamplingAuraFlow",
                ["inputs"] = auraFlowInputs
            },
            ["6"] = new JsonObject
            {
                ["class_type"] = "TextEncodeQwenImageEditPlus",
                ["inputs"] = positiveInputs
            },
            ["7"] = new JsonObject
            {
                ["class_type"] = "TextEncodeQwenImageEditPlus",
                ["inputs"] = negativeInputs
            },
            ["8"] = new JsonObject
            {
                ["class_type"] = "VAEEncode",
                ["inputs"] = new JsonObject { ["pixels"] = new JsonArray("2", 0), ["vae"] = new JsonArray("11", 0) }
            },
            ["9"] = new JsonObject
            {
                ["class_type"] = "SaveImage",
                ["inputs"] = new JsonObject { ["images"] = new JsonArray("15", 0), ["filename_prefix"] = "dreamgen_app/qwen-edit" }
            },
            ["10"] = new JsonObject
            {
                ["class_type"] = "CLIPLoader",
                ["inputs"] = new JsonObject { ["clip_name"] = model.TextEncoder, ["type"] = "qwen_image", ["device"] = "default" }
            },
            ["11"] = new JsonObject
            {
                ["class_type"] = "VAELoader",
                ["inputs"] = new JsonObject { ["vae_name"] = model.Vae }
            },
            ["12"] = new JsonObject
            {
                ["class_type"] = "FluxKontextMultiReferenceLatentMethod",
                ["inputs"] = new JsonObject { ["conditioning"] = new JsonArray("6", 0), ["reference_latents_method"] = "index_timestep_zero" }
            },
            ["13"] = new JsonObject
            {
                ["class_type"] = "FluxKontextMultiReferenceLatentMethod",
                ["inputs"] = new JsonObject { ["conditioning"] = new JsonArray("7", 0), ["reference_latents_method"] = "index_timestep_zero" }
            },
            ["14"] = new JsonObject
            {
                ["class_type"] = "CFGNorm",
                ["inputs"] = new JsonObject { ["model"] = new JsonArray("5", 0), ["strength"] = model.CfgNormStrength }
            },
            ["15"] = new JsonObject
            {
                ["class_type"] = "VAEDecode",
                ["inputs"] = new JsonObject { ["samples"] = new JsonArray("3", 0), ["vae"] = new JsonArray("11", 0) }
            }
        };

        // Optional editor LoRA. A LoRA alters BOTH the diffusion model and the CLIP, so all three
        // consumers are re-wired to the LoraLoader: the sampler's model branch (through CFGNorm) and the
        // positive AND negative text encodes. Leaving the clip inputs on node 10 would apply the LoRA to
        // the image path only. No configured name emits no node at all (a configured state, not a
        // fallback); a name without a strength fails fast here instead of inventing a weight.
        if (!string.IsNullOrWhiteSpace(model.LoraName))
        {
            var loraStrength = model.LoraStrength
                ?? throw new InvalidOperationException(
                    $"Image editor model '{model.ModelIdentifier}' configures editor LoRA '{model.LoraName}' without a strength. Set 'Editor LoRA Strength' in Model Manager (/model-manager).");

            workflow["17"] = new JsonObject
            {
                ["class_type"] = "LoraLoader",
                ["inputs"] = new JsonObject
                {
                    ["model"] = new JsonArray("4", 0),
                    ["clip"] = new JsonArray("10", 0),
                    ["lora_name"] = model.LoraName,
                    ["strength_model"] = loraStrength,
                    ["strength_clip"] = loraStrength
                }
            };

            positiveInputs["clip"] = new JsonArray("17", 1);
            negativeInputs["clip"] = new JsonArray("17", 1);
            auraFlowInputs["model"] = new JsonArray("17", 0);
        }

        AddReferenceLoaders(workflow, references);
        return workflow;
    }

    /// <summary>
    /// Selects the ComfyUI graph for a resolved editor model from its configured
    /// <see cref="ResolvedImageEditorModel.GraphKind"/> (Model Manager, persisted per model). The graph is
    /// never inferred from artifact names: an unconfigured kind fails fast here, and the resolver already
    /// rejects a ComfyUI-protocol editor that has none configured.
    /// </summary>
    internal static JsonObject BuildResolvedWorkflow(
        ResolvedImageEditorModel model,
        string sourceImageName,
        string instruction,
        IReadOnlyList<string>? referenceImageNames = null) => model.GraphKind switch
        {
            ImageEditorGraphKind.SplitUnet =>
                BuildWorkflow(model, sourceImageName, instruction, referenceImageNames),
            ImageEditorGraphKind.MergedCheckpoint =>
                BuildAioMergedCheckpointWorkflow(model, sourceImageName, instruction, referenceImageNames),
            _ => throw new InvalidOperationException(
                $"Image editor model '{model.ModelIdentifier}' has no editor graph kind configured. Set 'Editor Graph' for it in Model Manager (/model-manager).")
        };

    /// <summary>
    /// Builds the Qwen-Image-Edit workflow for a merged AIO checkpoint
    /// (e.g. <c>Qwen-Rapid-AIO-NSFW-v23.safetensors</c>) which bundles model+clip+vae in one file
    /// (B-101 MODEL DECISION). Uses <c>CheckpointLoaderSimple</c> (model/clip/vae together) instead
    /// of the split (<c>UNETLoader</c>+<c>CLIPLoader</c>+<c>VAELoader</c>) graph. Used by the RunPod
    /// serverless editing client, which accepts only this graph, and by any ComfyUI-protocol editor
    /// configured with <see cref="ImageEditorGraphKind.MergedCheckpoint"/>. The checkpoint name comes
    /// from the resolved <c>DiffusionModel</c> field; sampler settings come from the resolved model,
    /// never hardcoded.
    /// </summary>
    internal static JsonObject BuildAioMergedCheckpointWorkflow(
        ResolvedImageEditorModel model,
        string sourceImageName,
        string instruction,
        IReadOnlyList<string>? referenceImageNames = null)
    {
        var references = referenceImageNames ?? [];
        var positiveInputs = new JsonObject
        {
            ["clip"] = new JsonArray("16", 1),
            ["vae"] = new JsonArray("16", 2),
            ["image1"] = new JsonArray("2", 0),
            ["prompt"] = instruction
        };
        var negativeInputs = new JsonObject
        {
            ["clip"] = new JsonArray("16", 1),
            ["vae"] = new JsonArray("16", 2),
            ["image1"] = new JsonArray("2", 0),
            ["prompt"] = string.Empty
        };
        AddReferenceInputs(positiveInputs, negativeInputs, references);

        // Hoisted so the optional editor LoRA below can re-point the sampler branch at the LoraLoader.
        var auraFlowInputs = new JsonObject
        {
            ["model"] = new JsonArray("16", 0),
            ["shift"] = model.AuraFlowShift
        };

        var workflow = new JsonObject
        {
            ["1"] = new JsonObject
            {
                ["class_type"] = "LoadImage",
                ["inputs"] = new JsonObject { ["image"] = sourceImageName }
            },
            ["2"] = new JsonObject
            {
                ["class_type"] = "FluxKontextImageScale",
                ["inputs"] = new JsonObject { ["image"] = new JsonArray("1", 0) }
            },
            ["3"] = new JsonObject
            {
                ["class_type"] = "KSampler",
                ["inputs"] = new JsonObject
                {
                    ["model"] = new JsonArray("14", 0),
                    ["positive"] = new JsonArray("12", 0),
                    ["negative"] = new JsonArray("13", 0),
                    ["latent_image"] = new JsonArray("8", 0),
                    ["seed"] = Random.Shared.NextInt64(long.MaxValue),
                    ["steps"] = model.Steps,
                    ["cfg"] = model.Cfg,
                    ["sampler_name"] = model.Sampler,
                    ["scheduler"] = model.Scheduler,
                    ["denoise"] = model.Denoise
                }
            },
            ["5"] = new JsonObject
            {
                ["class_type"] = "ModelSamplingAuraFlow",
                ["inputs"] = auraFlowInputs
            },
            ["6"] = new JsonObject
            {
                ["class_type"] = "TextEncodeQwenImageEditPlus",
                ["inputs"] = positiveInputs
            },
            ["7"] = new JsonObject
            {
                ["class_type"] = "TextEncodeQwenImageEditPlus",
                ["inputs"] = negativeInputs
            },
            ["8"] = new JsonObject
            {
                ["class_type"] = "VAEEncode",
                ["inputs"] = new JsonObject { ["pixels"] = new JsonArray("2", 0), ["vae"] = new JsonArray("16", 2) }
            },
            ["9"] = new JsonObject
            {
                ["class_type"] = "SaveImage",
                ["inputs"] = new JsonObject { ["images"] = new JsonArray("15", 0), ["filename_prefix"] = "dreamgen_app/qwen-edit" }
            },
            ["12"] = new JsonObject
            {
                ["class_type"] = "FluxKontextMultiReferenceLatentMethod",
                ["inputs"] = new JsonObject { ["conditioning"] = new JsonArray("6", 0), ["reference_latents_method"] = "index_timestep_zero" }
            },
            ["13"] = new JsonObject
            {
                ["class_type"] = "FluxKontextMultiReferenceLatentMethod",
                ["inputs"] = new JsonObject { ["conditioning"] = new JsonArray("7", 0), ["reference_latents_method"] = "index_timestep_zero" }
            },
            ["14"] = new JsonObject
            {
                ["class_type"] = "CFGNorm",
                ["inputs"] = new JsonObject { ["model"] = new JsonArray("5", 0), ["strength"] = model.CfgNormStrength }
            },
            ["15"] = new JsonObject
            {
                ["class_type"] = "VAEDecode",
                ["inputs"] = new JsonObject { ["samples"] = new JsonArray("3", 0), ["vae"] = new JsonArray("16", 2) }
            },
            ["16"] = new JsonObject
            {
                ["class_type"] = "CheckpointLoaderSimple",
                ["inputs"] = new JsonObject { ["ckpt_name"] = model.DiffusionModel }
            }
        };

        // Optional editor LoRA. A LoRA alters BOTH the diffusion model and the CLIP, so all three
        // consumers are re-wired to the LoraLoader: the sampler's model branch (through CFGNorm) and the
        // positive AND negative text encodes. Leaving the clip inputs on node 16 would apply the LoRA to
        // the image path only. No configured name emits no node at all (a configured state, not a
        // fallback); a name without a strength fails fast here instead of inventing a weight.
        if (!string.IsNullOrWhiteSpace(model.LoraName))
        {
            var loraStrength = model.LoraStrength
                ?? throw new InvalidOperationException(
                    $"Image editor model '{model.ModelIdentifier}' configures editor LoRA '{model.LoraName}' without a strength. Set 'Editor LoRA Strength' in Model Manager (/model-manager).");

            workflow["17"] = new JsonObject
            {
                ["class_type"] = "LoraLoader",
                ["inputs"] = new JsonObject
                {
                    ["model"] = new JsonArray("16", 0),
                    ["clip"] = new JsonArray("16", 1),
                    ["lora_name"] = model.LoraName,
                    ["strength_model"] = loraStrength,
                    ["strength_clip"] = loraStrength
                }
            };

            positiveInputs["clip"] = new JsonArray("17", 1);
            negativeInputs["clip"] = new JsonArray("17", 1);
            auraFlowInputs["model"] = new JsonArray("17", 0);
        }

        AddReferenceLoaders(workflow, references);
        return workflow;
    }

    private static void AddReferenceInputs(
        JsonObject positiveInputs,
        JsonObject negativeInputs,
        IReadOnlyList<string> referenceImageNames)
    {
        for (var index = 0; index < referenceImageNames.Count; index++)
        {
            var nodeId = (20 + index).ToString();
            positiveInputs[$"image{index + 2}"] = new JsonArray(nodeId, 0);
            negativeInputs[$"image{index + 2}"] = new JsonArray(nodeId, 0);
        }
    }

    private static void AddReferenceLoaders(JsonObject workflow, IReadOnlyList<string> referenceImageNames)
    {
        for (var index = 0; index < referenceImageNames.Count; index++)
        {
            workflow[(20 + index).ToString()] = new JsonObject
            {
                ["class_type"] = "LoadImage",
                ["inputs"] = new JsonObject { ["image"] = referenceImageNames[index] }
            };
        }
    }

    public async Task<byte[]> EditAsync(
        ResolvedImageEditorModel model,
        Stream sourceImage,
        string sourceFileName,
        string instruction,
        CancellationToken cancellationToken = default)
    {
        if (sourceImage is null || !sourceImage.CanRead)
            throw new ImageGenerationException("The source image cannot be read.", model.ProviderName, reasonCode: "source_image_unreadable");
        if (string.IsNullOrWhiteSpace(sourceFileName))
            throw new ImageGenerationException("The source image file name is required.", model.ProviderName, reasonCode: "source_image_name_missing");
        if (string.IsNullOrWhiteSpace(instruction))
            throw new ImageGenerationException("An image edit instruction is required.", model.ProviderName, reasonCode: "instruction_missing");

        var baseUrl = model.ComfyUiUrl.TrimEnd('/');
        var client = _httpClientFactory.CreateClient("CompletionClient");
        client.Timeout = TimeSpan.FromSeconds(model.ProviderTimeoutSeconds);
        if (!string.IsNullOrWhiteSpace(model.ApiKeyEncrypted))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _encryptionService.Decrypt(model.ApiKeyEncrypted));
        }

        try
        {
            var uploadedName = await ComfyUiWorkflowTransport.UploadImageAsync(
                client, baseUrl, sourceImage, sourceFileName, model.ProviderName, EditReasonPrefix, cancellationToken);
            var workflow = BuildResolvedWorkflow(model, uploadedName, instruction.Trim());

            _logger.LogInformation("ComfyUI source-image edit start: Provider={ProviderName}, DiffusionModel={DiffusionModel}, InstructionChars={InstructionChars}", model.ProviderName, model.DiffusionModel, instruction.Length);
            var promptId = await ComfyUiWorkflowTransport.SubmitPromptAsync(
                client, baseUrl, workflow, "dreamgen-app", model.ProviderName, EditReasonPrefix, cancellationToken);

            var history = await ComfyUiWorkflowTransport.WaitForHistoryAsync(
                client, baseUrl, promptId, model.ProviderTimeoutSeconds, model.ProviderName, EditReasonPrefix, cancellationToken);
            return await ComfyUiWorkflowTransport.DownloadOutputAsync(
                client, baseUrl, history, promptId, model.ProviderName, EditReasonPrefix, cancellationToken);
        }
        catch (ImageGenerationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ComfyUI source-image edit failed: Provider={ProviderName}", model.ProviderName);
            throw new ImageGenerationException($"ComfyUI source-image edit failed: {ex.Message}", model.ProviderName, reasonCode: "comfyui_edit_client_error", inner: ex);
        }
    }

    public async Task<byte[]> EditWithReferencesAsync(
        ResolvedImageEditorModel model,
        Stream sourceImage,
        string sourceFileName,
        string instruction,
        IReadOnlyList<ImageEditingReference> references,
        CancellationToken cancellationToken = default)
    {
        ValidateEditInputs(model, sourceImage, sourceFileName, instruction);
        ValidateReferences(references);

        var baseUrl = model.ComfyUiUrl.TrimEnd('/');
        var client = _httpClientFactory.CreateClient("CompletionClient");
        client.Timeout = TimeSpan.FromSeconds(model.ProviderTimeoutSeconds);
        if (!string.IsNullOrWhiteSpace(model.ApiKeyEncrypted))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _encryptionService.Decrypt(model.ApiKeyEncrypted));
        }

        try
        {
            var uploadedSourceName = await ComfyUiWorkflowTransport.UploadImageAsync(
                client, baseUrl, sourceImage, sourceFileName, model.ProviderName, EditReasonPrefix, cancellationToken);
            var uploadedReferenceNames = new List<string>(references.Count);
            foreach (var reference in references.OrderBy(reference => reference.Ordinal))
            {
                uploadedReferenceNames.Add(await ComfyUiWorkflowTransport.UploadImageAsync(
                    client, baseUrl, reference.Image, reference.FileName, model.ProviderName, EditReasonPrefix, cancellationToken));
            }

            var workflow = BuildResolvedWorkflow(model, uploadedSourceName, instruction.Trim(), uploadedReferenceNames);
            var promptId = await ComfyUiWorkflowTransport.SubmitPromptAsync(
                client, baseUrl, workflow, "dreamgen-app", model.ProviderName, EditReasonPrefix, cancellationToken);

            var history = await ComfyUiWorkflowTransport.WaitForHistoryAsync(
                client, baseUrl, promptId, model.ProviderTimeoutSeconds, model.ProviderName, EditReasonPrefix, cancellationToken);
            return await ComfyUiWorkflowTransport.DownloadOutputAsync(
                client, baseUrl, history, promptId, model.ProviderName, EditReasonPrefix, cancellationToken);
        }
        catch (ImageGenerationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ComfyUI reference source-image edit failed: Provider={ProviderName}", model.ProviderName);
            throw new ImageGenerationException($"ComfyUI reference source-image edit failed: {ex.Message}", model.ProviderName, reasonCode: "comfyui_edit_client_error", inner: ex);
        }
    }

    private static void ValidateEditInputs(ResolvedImageEditorModel model, Stream sourceImage, string sourceFileName, string instruction)
    {
        if (sourceImage is null || !sourceImage.CanRead)
            throw new ImageGenerationException("The source image cannot be read.", model.ProviderName, reasonCode: "source_image_unreadable");
        if (string.IsNullOrWhiteSpace(sourceFileName))
            throw new ImageGenerationException("The source image file name is required.", model.ProviderName, reasonCode: "source_image_name_missing");
        if (string.IsNullOrWhiteSpace(instruction))
            throw new ImageGenerationException("An image edit instruction is required.", model.ProviderName, reasonCode: "instruction_missing");
    }

    private static void ValidateReferences(IReadOnlyList<ImageEditingReference> references)
    {
        if (references is null || references.Count == 0)
            throw new InvalidOperationException("At least one ordered image editing reference is required.");
        if (references.Any(reference => reference.Ordinal <= 0 || string.IsNullOrWhiteSpace(reference.SemanticRole)
            || reference.Image is null || !reference.Image.CanRead || string.IsNullOrWhiteSpace(reference.FileName)
            || string.IsNullOrWhiteSpace(reference.Checksum)))
            throw new InvalidOperationException("Every image editing reference requires an ordinal, semantic role, readable image, file name, and checksum.");
        if (references.Select(reference => reference.Ordinal).Distinct().Count() != references.Count)
            throw new InvalidOperationException("Image editing reference ordinals must be unique.");
    }
}