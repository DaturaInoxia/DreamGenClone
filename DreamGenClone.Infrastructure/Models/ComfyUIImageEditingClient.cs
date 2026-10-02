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
        IReadOnlyList<string>? referenceImageNames = null,
        string? maskImageName = null,
        ImageEditingMask? mask = null)
    {
        // Confinement is implemented in ONE graph. Saying so here, with the fix named, beats emitting a graph that
        // quietly ignores the region and returns an edit of the whole frame.
        if (!string.IsNullOrWhiteSpace(maskImageName) && model.GraphKind != ImageEditorGraphKind.QwenImage21Native)
        {
            throw new InvalidOperationException(
                $"Image editor model '{model.ModelIdentifier}' cannot apply a region mask: confinement is implemented only "
                + "in the Qwen-Image-2.1 native graph (VAEEncodeForInpaint over a masked latent). Set 'Editor Graph' to "
                + "Qwen-Image-2.1 for this model in Model Manager (/model-manager), or edit without a region.");
        }

        return model.GraphKind switch
        {
            ImageEditorGraphKind.SplitUnet =>
                BuildWorkflow(model, sourceImageName, instruction, referenceImageNames),
            ImageEditorGraphKind.MergedCheckpoint =>
                BuildAioMergedCheckpointWorkflow(model, sourceImageName, instruction, referenceImageNames),
            ImageEditorGraphKind.QwenImage21Native =>
                BuildQwenImage21EditWorkflow(model, sourceImageName, instruction, referenceImageNames, maskImageName, mask),
            _ => throw new InvalidOperationException(
                $"Image editor model '{model.ModelIdentifier}' has no editor graph kind configured. Set 'Editor Graph' for it in Model Manager (/model-manager).")
        };
    }

    /// <summary>
    /// Qwen-Image-2.1 native edit graph: the source image and every reference travel through ONE
    /// <c>TextEncodeQwenImage21</c> node that also returns the latent the sampler consumes. The source
    /// occupies <c>images.image_1</c> (it is both the edit target and the latent's shape source) and
    /// reference i occupies <c>images.image_{i+2}</c>. <c>QwenImage21Cache</c> sits between the UNET and
    /// the sampler (prefix KV cache).
    ///
    /// Reference sub-inputs are declared as flat, dotted autogrow ids - the ONLY shape ComfyUI accepts.
    /// Flat <c>image_N</c> kwargs raise a TypeError inside execute() and a hand-built <c>images</c> dict is
    /// silently ignored, so a sloppy wiring would DROP every reference without failing; both were ruled
    /// out on the host 2026-09-22. <c>resolution</c> is a pixel BUDGET, not a dimension.
    /// </summary>
    internal static JsonObject BuildQwenImage21EditWorkflow(
        ResolvedImageEditorModel model,
        string sourceImageName,
        string instruction,
        IReadOnlyList<string>? referenceImageNames = null,
        string? maskImageName = null,
        ImageEditingMask? mask = null)
    {
        var resolutionBudget = model.ResolutionBudget
            ?? throw new InvalidOperationException(
                $"Image editor model '{model.ModelIdentifier}' uses the Qwen-Image-2.1 native graph but has no reference "
                + "resolution budget. Add 'Resolution' to its NativeMultiReference qualification in Model Manager (/model-manager).");

        var encodeInputs = new JsonObject
        {
            ["clip"] = new JsonArray("3", 0),
            ["vae"] = new JsonArray("4", 0),
            ["prompt"] = instruction,
            ["negative_prompt"] = string.Empty,
            ["resolution"] = resolutionBudget,
            ["images.image_1"] = new JsonArray("1", 0)
        };

        var wf = new JsonObject
        {
            ["1"] = new JsonObject
            {
                ["class_type"] = "LoadImage",
                ["inputs"] = new JsonObject { ["image"] = sourceImageName }
            },
            ["2"] = new JsonObject
            {
                ["class_type"] = "UNETLoader",
                ["inputs"] = new JsonObject { ["unet_name"] = model.DiffusionModel, ["weight_dtype"] = "default" }
            },
            ["3"] = new JsonObject
            {
                ["class_type"] = "CLIPLoader",
                ["inputs"] = new JsonObject { ["clip_name"] = model.TextEncoder, ["type"] = "qwen_image", ["device"] = "default" }
            },
            ["4"] = new JsonObject
            {
                ["class_type"] = "VAELoader",
                ["inputs"] = new JsonObject { ["vae_name"] = model.Vae }
            },
            ["5"] = new JsonObject
            {
                ["class_type"] = "QwenImage21Cache",
                ["inputs"] = new JsonObject { ["model"] = new JsonArray("2", 0), ["device"] = "auto", ["dtype"] = "default" }
            },
            ["6"] = new JsonObject
            {
                ["class_type"] = "TextEncodeQwenImage21",
                ["inputs"] = encodeInputs
            },
            ["7"] = new JsonObject
            {
                ["class_type"] = "KSampler",
                ["inputs"] = new JsonObject
                {
                    ["seed"] = Random.Shared.NextInt64(long.MaxValue),
                    ["steps"] = model.Steps,
                    ["cfg"] = model.Cfg,
                    ["sampler_name"] = model.Sampler,
                    ["scheduler"] = model.Scheduler,
                    ["denoise"] = model.Denoise,
                    ["model"] = new JsonArray("5", 0),
                    ["positive"] = new JsonArray("6", 0),
                    ["negative"] = new JsonArray("6", 1),
                    ["latent_image"] = new JsonArray("6", 2)
                }
            },
            ["8"] = new JsonObject
            {
                ["class_type"] = "VAEDecode",
                ["inputs"] = new JsonObject { ["samples"] = new JsonArray("7", 0), ["vae"] = new JsonArray("4", 0) }
            },
            ["9"] = new JsonObject
            {
                ["class_type"] = "SaveImage",
                ["inputs"] = new JsonObject { ["images"] = new JsonArray("8", 0), ["filename_prefix"] = "dreamgen_app/qwen21-edit" }
            }
        };

        if (referenceImageNames is { Count: > 0 })
        {
            for (var index = 0; index < referenceImageNames.Count; index++)
            {
                var nodeId = (20 + index).ToString();
                wf[nodeId] = new JsonObject
                {
                    ["class_type"] = "LoadImage",
                    ["inputs"] = new JsonObject { ["image"] = referenceImageNames[index] }
                };
                encodeInputs[$"images.image_{index + 2}"] = new JsonArray(nodeId, 0);
            }
        }

        // A REGION swaps the sampler's starting latent (CASE-21's mechanism, node for node). TextEncodeQwenImage21's
        // own latent carries the source as a REFERENCE, so without this the sampler is free to regenerate the whole
        // frame - measured, and the reason a region named only in the instruction cannot contain an edit (CASE-23).
        // Node ids start at 40 because references already occupy 20+; grow_mask_by is the operator's value, and
        // FeatherMask is emitted ONLY when they asked for feathering, because a hidden default here would soften (or
        // harden) every region edit without anybody asking.
        if (!string.IsNullOrWhiteSpace(maskImageName) && mask is not null)
        {
            wf["40"] = new JsonObject
            {
                ["class_type"] = "LoadImageMask",
                ["inputs"] = new JsonObject { ["image"] = maskImageName, ["channel"] = "red" }
            };

            // The mask is grown and feathered when it is BUILT (the region engine bakes both), so the graph reads it
            // as-is: grow_mask_by stays 0 and no FeatherMask node is emitted. The host FeatherMask node feathers the
            // mask tensor's FRAME border, not a region drawn inside it, so it cannot soften an interior rectangle's
            // edge (the measured visible-seam defect).
            wf["42"] = new JsonObject
            {
                ["class_type"] = "VAEEncodeForInpaint",
                ["inputs"] = new JsonObject
                {
                    ["pixels"] = new JsonArray("1", 0),
                    ["vae"] = new JsonArray("4", 0),
                    ["mask"] = new JsonArray("40", 0),
                    ["grow_mask_by"] = 0
                }
            };

            ((JsonObject)wf["7"]!["inputs"]!)["latent_image"] = new JsonArray("42", 0);
        }

        // Editor LoRA feeds the MODEL and the CLIP: wiring only the model branch would silently apply
        // the LoRA to half the stack (the rule the 2511 editor path learned the hard way).
        if (!string.IsNullOrWhiteSpace(model.LoraName))
        {
            var strength = model.LoraStrength
                ?? throw new InvalidOperationException(
                    $"Image editor model '{model.ModelIdentifier}' configures editor LoRA '{model.LoraName}' without an explicit strength.");

            wf["17"] = new JsonObject
            {
                ["class_type"] = "LoraLoader",
                ["inputs"] = new JsonObject
                {
                    ["lora_name"] = model.LoraName,
                    ["strength_model"] = strength,
                    ["strength_clip"] = strength,
                    ["model"] = new JsonArray("5", 0),
                    ["clip"] = new JsonArray("3", 0)
                }
            };
            wf["7"]!["inputs"]!["model"] = new JsonArray("17", 0);
            encodeInputs["clip"] = new JsonArray("17", 0);
        }

        return wf;
    }

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
        CancellationToken cancellationToken = default,
        ImageEditingMask? mask = null)
    {
        if (sourceImage is null || !sourceImage.CanRead)
            throw new ImageGenerationException("The source image cannot be read.", model.ProviderName, reasonCode: "source_image_unreadable");
        if (string.IsNullOrWhiteSpace(sourceFileName))
            throw new ImageGenerationException("The source image file name is required.", model.ProviderName, reasonCode: "source_image_name_missing");
        if (string.IsNullOrWhiteSpace(instruction))
            throw new ImageGenerationException("An image edit instruction is required.", model.ProviderName, reasonCode: "instruction_missing");
        ValidateMask(mask, model);

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
            var uploadedMaskName = await UploadMaskAsync(client, baseUrl, model, mask, cancellationToken);
            var workflow = BuildResolvedWorkflow(model, uploadedName, instruction.Trim(), maskImageName: uploadedMaskName, mask: mask);

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
        CancellationToken cancellationToken = default,
        ImageEditingMask? mask = null)
    {
        ValidateEditInputs(model, sourceImage, sourceFileName, instruction);
        ValidateReferences(references);
        ValidateMask(mask, model);

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

            var uploadedMaskName = await UploadMaskAsync(client, baseUrl, model, mask, cancellationToken);
            var workflow = BuildResolvedWorkflow(model, uploadedSourceName, instruction.Trim(), uploadedReferenceNames, uploadedMaskName, mask);
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

    /// <summary>
    /// A region mask must be complete, and its geometry must be one the HOST can honour: VAEEncodeForInpaint's own
    /// <c>grow_mask_by</c> accepts 0-64. A value outside that range fails here with the reason, rather than as a
    /// silent clamp on the far side of the wire where nobody would see it.
    /// </summary>
    private static void ValidateMask(ImageEditingMask? mask, ResolvedImageEditorModel model)
    {
        if (mask is null)
            return;
        if (mask.Mask is null || !mask.Mask.CanRead)
            throw new ImageGenerationException("The region mask cannot be read.", model.ProviderName, reasonCode: "mask_unreadable");
        if (string.IsNullOrWhiteSpace(mask.FileName))
            throw new ImageGenerationException("The region mask file name is required.", model.ProviderName, reasonCode: "mask_name_missing");
        if (string.IsNullOrWhiteSpace(mask.Checksum))
            throw new ImageGenerationException("The region mask checksum is required.", model.ProviderName, reasonCode: "mask_checksum_missing");
        if (mask.GrowMaskBy is < 0 or > 64)
            throw new ImageGenerationException(
                $"The region mask 'grow' value must be between 0 and 64, but was {mask.GrowMaskBy}.", model.ProviderName, reasonCode: "mask_grow_out_of_range");
        if (mask.FeatherPixels < 0)
            throw new ImageGenerationException(
                $"The region mask feather must not be negative, but was {mask.FeatherPixels}.", model.ProviderName, reasonCode: "mask_feather_negative");
    }

    /// <summary>
    /// Uploads the mask to the host's input folder, or returns null when there is no mask. ComfyUI reads a mask by input
    /// FILE NAME exactly as it reads the source image, so a mask that was never uploaded cannot be referenced by the
    /// graph - and the upload is the reason the graph takes a NAME rather than bytes.
    /// </summary>
    private static Task<string?> UploadMaskAsync(
        HttpClient client,
        string baseUrl,
        ResolvedImageEditorModel model,
        ImageEditingMask? mask,
        CancellationToken cancellationToken)
        => mask is null
            ? Task.FromResult<string?>(null)
            : ComfyUiWorkflowTransport.UploadImageAsync(
                client, baseUrl, mask.Mask, mask.FileName, model.ProviderName, EditReasonPrefix, cancellationToken)!;

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