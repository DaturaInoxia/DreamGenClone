using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.Models;

/// <summary>
/// ComfyUI image generation client. Implements <see cref="IImageGenerationClient"/> using the
/// ComfyUI HTTP API: POST a workflow to <c>/prompt</c>, poll <c>/history/{prompt_id}</c> until
/// success, then fetch the produced PNG via <c>/view</c>.
/// </summary>
public sealed class ComfyUIImageClient : IImageGenerationClient, IReferenceConditionedImageClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApiKeyEncryptionService _encryptionService;
    private readonly ILogger<ComfyUIImageClient> _logger;

    public ComfyUIImageClient(
        IHttpClientFactory httpClientFactory,
        IApiKeyEncryptionService encryptionService,
        ILogger<ComfyUIImageClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    /// <summary>
    /// Default PonyV6 text-to-image workflow (node ids 4/10/6/7/3/5/8/9). The checkpoint name and
    /// positive/negative prompts are injected at call time. CLIP skip 2 via CLIPSetLastLayer.
    /// When <paramref name="seed"/> is provided the KSampler uses that fixed seed (reproducible);
    /// otherwise it draws a random seed each call.
    /// </summary>
    internal static JsonObject BuildDefaultWorkflow(string checkpointName, string prompt, string negative, string? size, long? seed)
    {
        var (width, height) = ParseSize(size);
        var wf = new JsonObject
        {
            ["4"] = new JsonObject
            {
                ["class_type"] = "CheckpointLoaderSimple",
                ["inputs"] = new JsonObject { ["ckpt_name"] = checkpointName }
            },
            ["10"] = new JsonObject
            {
                ["class_type"] = "CLIPSetLastLayer",
                ["inputs"] = new JsonObject { ["stop_at_clip_layer"] = -2, ["clip"] = new JsonArray("4", 1) }
            },
            ["6"] = new JsonObject
            {
                ["class_type"] = "CLIPTextEncode",
                ["inputs"] = new JsonObject { ["text"] = prompt, ["clip"] = new JsonArray("10", 0) }
            },
            ["7"] = new JsonObject
            {
                ["class_type"] = "CLIPTextEncode",
                ["inputs"] = new JsonObject { ["text"] = negative, ["clip"] = new JsonArray("10", 0) }
            },
            ["3"] = new JsonObject
            {
                ["class_type"] = "KSampler",
                ["inputs"] = new JsonObject
                {
                    ["seed"] = seed ?? Random.Shared.Next(0, int.MaxValue),
                    ["steps"] = 25,
                    ["cfg"] = 7.0,
                    ["sampler_name"] = "euler_ancestral",
                    ["scheduler"] = "normal",
                    ["denoise"] = 1.0,
                    ["model"] = new JsonArray("4", 0),
                    ["positive"] = new JsonArray("6", 0),
                    ["negative"] = new JsonArray("7", 0),
                    ["latent_image"] = new JsonArray("5", 0)
                }
            },
            ["5"] = new JsonObject
            {
                ["class_type"] = "EmptyLatentImage",
                ["inputs"] = new JsonObject { ["width"] = width, ["height"] = height, ["batch_size"] = 1 }
            },
            ["8"] = new JsonObject
            {
                ["class_type"] = "VAEDecode",
                ["inputs"] = new JsonObject { ["samples"] = new JsonArray("3", 0), ["vae"] = new JsonArray("4", 2) }
            },
            ["9"] = new JsonObject
            {
                ["class_type"] = "SaveImage",
                ["inputs"] = new JsonObject { ["filename_prefix"] = "dreamgen_app", ["images"] = new JsonArray("8", 0) }
            }
        };
        return wf;
    }

    /// <summary>
    /// SDXL/Juggernaut text-to-image workflow (node ids 4/6/7/3/5/8/9). Sampler/CLIP values follow
    /// the studio-configurable <paramref name="options"/> when provided, otherwise the Juggernaut
    /// defaults (DPM++ 2M SDE / karras / 30 steps / CFG 5, no CLIP skip). A CLIPSetLastLayer node
    /// (id 13) is added only when a clip-skip value is set. The checkpoint name and
    /// positive/negative prompts are injected at call time.
    /// </summary>
    internal static JsonObject BuildSdxlWorkflow(string checkpointName, string prompt, string negative, string? size, long? seed, SceneImageGenerationOptions? options = null)
    {
        var (width, height) = ParseSize(size);
        var cfg = options?.Cfg ?? 5.0;
        var steps = options?.Steps ?? 30;
        var sampler = string.IsNullOrWhiteSpace(options?.SamplerName) ? "dpmpp_2m_sde" : options.SamplerName;
        var scheduler = string.IsNullOrWhiteSpace(options?.Scheduler) ? "karras" : options.Scheduler;
        var clipNodeId = options?.ClipSkip is not null ? "13" : "4";
        var clipSlot = options?.ClipSkip is not null ? 0 : 1;

        var wf = new JsonObject
        {
            ["4"] = new JsonObject
            {
                ["class_type"] = "CheckpointLoaderSimple",
                ["inputs"] = new JsonObject { ["ckpt_name"] = checkpointName }
            },
            ["6"] = new JsonObject
            {
                ["class_type"] = "CLIPTextEncode",
                ["inputs"] = new JsonObject { ["text"] = prompt, ["clip"] = new JsonArray(clipNodeId, clipSlot) }
            },
            ["7"] = new JsonObject
            {
                ["class_type"] = "CLIPTextEncode",
                ["inputs"] = new JsonObject { ["text"] = negative, ["clip"] = new JsonArray(clipNodeId, clipSlot) }
            },
            ["3"] = new JsonObject
            {
                ["class_type"] = "KSampler",
                ["inputs"] = new JsonObject
                {
                    ["seed"] = seed ?? Random.Shared.Next(0, int.MaxValue),
                    ["steps"] = steps,
                    ["cfg"] = cfg,
                    ["sampler_name"] = sampler,
                    ["scheduler"] = scheduler,
                    ["denoise"] = 1.0,
                    ["model"] = new JsonArray("4", 0),
                    ["positive"] = new JsonArray("6", 0),
                    ["negative"] = new JsonArray("7", 0),
                    ["latent_image"] = new JsonArray("5", 0)
                }
            },
            ["5"] = new JsonObject
            {
                ["class_type"] = "EmptyLatentImage",
                ["inputs"] = new JsonObject { ["width"] = width, ["height"] = height, ["batch_size"] = 1 }
            },
            ["8"] = new JsonObject
            {
                ["class_type"] = "VAEDecode",
                ["inputs"] = new JsonObject { ["samples"] = new JsonArray("3", 0), ["vae"] = new JsonArray("4", 2) }
            },
            ["9"] = new JsonObject
            {
                ["class_type"] = "SaveImage",
                ["inputs"] = new JsonObject { ["filename_prefix"] = "dreamgen_app", ["images"] = new JsonArray("8", 0) }
            }
        };

        if (options?.ClipSkip is { } skip)
        {
            wf["13"] = new JsonObject
            {
                ["class_type"] = "CLIPSetLastLayer",
                ["inputs"] = new JsonObject { ["stop_at_clip_layer"] = skip, ["clip"] = new JsonArray("4", 1) }
            };
        }

        return wf;
    }

    /// <summary>
    /// FLUX.1-dev text-to-image workflow (split loaders; mirrors
    /// helpers/flux-local-host/flux-t2i-proof.json). Uses DualCLIPLoader (t5xxl_fp8_e4m3fn + clip_l,
    /// type "flux") -> FluxGuidance 3.5 -> KSampler cfg 1.0 / euler / simple / 28 steps with an EMPTY
    /// negative (FLUX does not use negatives — describe the desired state positively), and the shared
    /// VAE (ae.safetensors). The diffusion model identifier is the UNETLoader unet_name. cfg stays 1.0;
    /// the real strength control is FluxGuidance. Resolution from <paramref name="size"/> (ParseSize
    /// default 1024x1024; FLUX requires multiples of 16). The LoRA unlock is intentionally NOT applied
    /// by default (stock-fp8 proof; non-explicit scenes).
    /// </summary>
    internal static JsonObject BuildFluxWorkflow(string unetName, string prompt, string? size, long? seed)
    {
        var (width, height) = ParseSize(size);
        var wf = new JsonObject
        {
            ["2"] = new JsonObject
            {
                ["class_type"] = "DualCLIPLoader",
                ["inputs"] = new JsonObject
                {
                    ["clip_name1"] = "t5xxl_fp8_e4m3fn.safetensors",
                    ["clip_name2"] = "clip_l.safetensors",
                    ["type"] = "flux"
                }
            },
            ["4"] = new JsonObject
            {
                ["class_type"] = "UNETLoader",
                ["inputs"] = new JsonObject { ["unet_name"] = unetName, ["weight_dtype"] = "default" }
            },
            ["6"] = new JsonObject
            {
                ["class_type"] = "CLIPTextEncode",
                ["inputs"] = new JsonObject { ["text"] = prompt, ["clip"] = new JsonArray("2", 0) }
            },
            ["7"] = new JsonObject
            {
                ["class_type"] = "CLIPTextEncode",
                ["inputs"] = new JsonObject { ["text"] = "", ["clip"] = new JsonArray("2", 0) }
            },
            ["8"] = new JsonObject
            {
                ["class_type"] = "FluxGuidance",
                ["inputs"] = new JsonObject { ["conditioning"] = new JsonArray("6", 0), ["guidance"] = 3.5 }
            },
            ["3"] = new JsonObject
            {
                ["class_type"] = "KSampler",
                ["inputs"] = new JsonObject
                {
                    ["seed"] = seed ?? Random.Shared.Next(0, int.MaxValue),
                    ["steps"] = 28,
                    ["cfg"] = 1.0,
                    ["sampler_name"] = "euler",
                    ["scheduler"] = "simple",
                    ["denoise"] = 1.0,
                    ["model"] = new JsonArray("4", 0),
                    ["positive"] = new JsonArray("8", 0),
                    ["negative"] = new JsonArray("7", 0),
                    ["latent_image"] = new JsonArray("5", 0)
                }
            },
            ["5"] = new JsonObject
            {
                ["class_type"] = "EmptyLatentImage",
                ["inputs"] = new JsonObject { ["width"] = width, ["height"] = height, ["batch_size"] = 1 }
            },
            ["10"] = new JsonObject
            {
                ["class_type"] = "VAELoader",
                ["inputs"] = new JsonObject { ["vae_name"] = "ae.safetensors" }
            },
            ["11"] = new JsonObject
            {
                ["class_type"] = "VAEDecode",
                ["inputs"] = new JsonObject { ["samples"] = new JsonArray("3", 0), ["vae"] = new JsonArray("10", 0) }
            },
            ["9"] = new JsonObject
            {
                ["class_type"] = "SaveImage",
                ["inputs"] = new JsonObject { ["filename_prefix"] = "dreamgen_app", ["images"] = new JsonArray("11", 0) }
            }
        };
        return wf;
    }

    /// <summary>
    /// Qwen-Image-2.1 workflow: ComfyUI's unified text-to-image + reference-conditioned generation,
    /// mirroring the Comfy-Org templates and the proven local proof graph
    /// (helpers/local-comfyui-host/run-qwen-2-1-proof.ps1).
    ///
    /// 2.1 is a split model behind ONE conditioning node. <c>TextEncodeQwenImage21</c> returns
    /// positive, negative AND the latent, and takes references as flat autogrow sub-inputs
    /// <c>images.image_1</c>, <c>images.image_2</c>, ... Two plausible-looking alternatives fail
    /// SILENTLY and were ruled out on the host 2026-09-22: flat <c>image_N</c> kwargs raise a TypeError
    /// inside execute(), and a hand-built <c>images</c> dict matches no declared input so every
    /// reference is dropped. Generation therefore draws its canvas from EmptyLatentImage while the
    /// references ride the conditioning.
    ///
    /// The official path runs cfg 1.0 (which makes the negative inert), euler/simple, and 25-50 steps. Those
    /// values come from the model's <see cref="QwenImage21Refs"/> - i.e. from its qualified Model Manager
    /// configuration - and are NOT taken from the studio's sampler controls. That is deliberate: the studio's
    /// sampler/cfg/steps are SDXL-family values, and passing them here ran this cfg-1-distilled model at cfg 5 /
    /// dpmpp_2m_sde / karras, which produced blown-out, grainy renders (reported 2026-09-24). FLUX's builder has
    /// the same posture - it takes no options at all.
    /// </summary>
    /// <param name="referenceImageNames">
    /// Reference images ALREADY uploaded to ComfyUI under these names (the caller uploads them).
    /// Null or empty builds a plain text-to-image graph with no reference slots.
    /// </param>
    internal static JsonObject BuildQwenImage21Workflow(
        string unetName,
        QwenImage21Refs refs,
        string prompt,
        string negative,
        string? size,
        long? seed,
        IReadOnlyList<string>? referenceImageNames = null)
    {
        var (width, height) = ParseSize(size);
        var steps = refs.Steps;
        var cfg = refs.Cfg;
        var sampler = refs.SamplerName;
        var scheduler = refs.Scheduler;

        var encodeInputs = new JsonObject
        {
            ["clip"] = new JsonArray("2", 0),
            ["prompt"] = prompt,
            ["negative_prompt"] = negative,
            ["resolution"] = refs.ResolutionBudget
        };

        var wf = new JsonObject
        {
            ["1"] = new JsonObject
            {
                ["class_type"] = "UNETLoader",
                ["inputs"] = new JsonObject { ["unet_name"] = unetName, ["weight_dtype"] = "default" }
            },
            ["2"] = new JsonObject
            {
                ["class_type"] = "CLIPLoader",
                ["inputs"] = new JsonObject { ["clip_name"] = refs.TextEncoderName, ["type"] = "qwen_image", ["device"] = "default" }
            },
            ["3"] = new JsonObject
            {
                ["class_type"] = "VAELoader",
                ["inputs"] = new JsonObject { ["vae_name"] = refs.VaeName }
            },
            ["4"] = new JsonObject
            {
                ["class_type"] = "TextEncodeQwenImage21",
                ["inputs"] = encodeInputs
            },
            ["5"] = new JsonObject
            {
                ["class_type"] = "EmptyLatentImage",
                ["inputs"] = new JsonObject { ["width"] = width, ["height"] = height, ["batch_size"] = 1 }
            },
            ["6"] = new JsonObject
            {
                ["class_type"] = "KSampler",
                ["inputs"] = new JsonObject
                {
                    ["seed"] = seed ?? Random.Shared.Next(0, int.MaxValue),
                    ["steps"] = steps,
                    ["cfg"] = cfg,
                    ["sampler_name"] = sampler,
                    ["scheduler"] = scheduler,
                    ["denoise"] = 1.0,
                    ["model"] = new JsonArray("1", 0),
                    ["positive"] = new JsonArray("4", 0),
                    ["negative"] = new JsonArray("4", 1),
                    ["latent_image"] = new JsonArray("5", 0)
                }
            },
            ["7"] = new JsonObject
            {
                ["class_type"] = "VAEDecode",
                ["inputs"] = new JsonObject { ["samples"] = new JsonArray("6", 0), ["vae"] = new JsonArray("3", 0) }
            },
            ["8"] = new JsonObject
            {
                ["class_type"] = "SaveImage",
                ["inputs"] = new JsonObject { ["filename_prefix"] = "dreamgen_app", ["images"] = new JsonArray("7", 0) }
            }
        };

        if (referenceImageNames is { Count: > 0 })
        {
            // The VAE is what turns each reference into a latent that rides the conditioning, so it is
            // wired ONLY when references exist - the published text-to-image template leaves it off.
            encodeInputs["vae"] = new JsonArray("3", 0);

            for (var index = 0; index < referenceImageNames.Count; index++)
            {
                var nodeId = (20 + index).ToString();
                wf[nodeId] = new JsonObject
                {
                    ["class_type"] = "LoadImage",
                    ["inputs"] = new JsonObject { ["image"] = referenceImageNames[index], ["upload"] = "image" }
                };
                encodeInputs[$"images.image_{index + 1}"] = new JsonArray(nodeId, 0);
            }
        }

        return wf;
    }

    private static (int Width, int Height) ParseSize(string? size)
    {
        if (!string.IsNullOrWhiteSpace(size))
        {
            var parts = size.Split('x', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2
                && int.TryParse(parts[0], out var w)
                && int.TryParse(parts[1], out var h)
                && w > 0 && h > 0)
            {
                return (w, h);
            }
        }
        return (1024, 1024);
    }

    public async Task<byte[]?> GenerateAsync(
        ResolvedImageModel model,
        string prompt,
        string? size,
        string? negativePrompt = null,
        long? seed = null,
        CancellationToken cancellationToken = default,
        SceneImageGenerationOptions? options = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var baseUrl = (model.ComfyUiUrl ?? model.ProviderBaseUrl).TrimEnd('/');

        try
        {
            var client = _httpClientFactory.CreateClient("CompletionClient");
            client.Timeout = TimeSpan.FromSeconds(model.ProviderTimeoutSeconds);

            if (!string.IsNullOrEmpty(model.ApiKeyEncrypted))
            {
                var decryptedKey = _encryptionService.Decrypt(model.ApiKeyEncrypted);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", decryptedKey);
            }

            // Checkpoint name: the model identifier is used verbatim when it looks like a ComfyUI
            // checkpoint filename. Any other identifier is a misconfiguration — fail fast, never a
            // silent default model.
            if (!LooksLikeFilename(model.ModelIdentifier))
            {
                throw new ImageGenerationException(
                    $"Model identifier '{model.ModelIdentifier}' is not a ComfyUI checkpoint filename. Configure the checkpoint filename as the model identifier in Model Manager.",
                    model.ProviderName,
                    reasonCode: "invalid_checkpoint_identifier");
            }
            var checkpoint = model.ModelIdentifier;

            // Model-family aware baseline negative: SDXL-family (Juggernaut/BigLust) and FLUX scene
            // images carry NO negative (model-author/BFL guidance, 2026-09-08 — see the sdxl + flux
            // instruction files); Pony keeps its short guard set. A provided per-scene negative
            // still takes precedence.
            ValidatePromptMetadata(model);
            var baselineNegative = model.SceneImageModelFamily switch
            {
                SceneImageModelFamily.Pony => "extra penis, multiple penises, two penises, duplicate anatomy, blurry, low quality, ugly, deformed, extra limbs, bad anatomy, watermark, text, censored, mosaic, airbrushed, plastic skin",
                _ => string.Empty
            };
            var effectiveNegative = string.IsNullOrWhiteSpace(negativePrompt)
                ? baselineNegative
                : negativePrompt.Trim();

            // Qwen-Image-2.1 is a SPLIT model: the model identifier is the DiT, while the text encoder,
            // the VAE and the reference pixel budget come from the model's NativeMultiReference
            // qualification. Missing configuration fails fast; no artifact name is ever guessed.
            var qwenImage21 = model.QwenImage21;
            if (model.SceneImageModelFamily == SceneImageModelFamily.QwenImage21 && qwenImage21 is null)
            {
                throw new ImageGenerationException(
                    $"Qwen-Image-2.1 model '{model.ModelIdentifier}' has no resolved text encoder / VAE / "
                    + "resolution qualification. Configure its NativeMultiReference qualification in Model Manager.",
                    model.ProviderName,
                    reasonCode: "missing_qwen_image_21_qualification");
            }

            // Select the workflow by model family: Pony keeps its CLIP-skip workflow; SDXL/Juggernaut
            // uses the no-CLIP-skip workflow. Unknown families fail fast (no fallback model).
            var workflow = model.SceneImageModelFamily switch
            {
                SceneImageModelFamily.Pony => BuildDefaultWorkflow(checkpoint, prompt, effectiveNegative, size, seed),
                SceneImageModelFamily.Sdxl => BuildSdxlWorkflow(checkpoint, prompt, effectiveNegative, size, seed, options),
                SceneImageModelFamily.Flux => BuildFluxWorkflow(checkpoint, prompt, size, seed),
                SceneImageModelFamily.QwenImage21 => BuildQwenImage21Workflow(
                    checkpoint, qwenImage21!, prompt, effectiveNegative, size, seed),
                _ => throw new ImageGenerationException(
                        $"Unsupported scene-image family '{model.SceneImageModelFamily}'. Configure the model family and prompt dialect in Model Manager.",
                    model.ProviderName,
                        reasonCode: "unsupported_image_family")
            };

            var payload = new JsonObject
            {
                ["prompt"] = workflow,
                ["client_id"] = "dreamgen-app"
            };

            return await SubmitWorkflowAndFetchAsync(client, baseUrl, payload, model, prompt.Length, cancellationToken);
        }
        catch (ImageGenerationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "ComfyUI image generation failed for provider {ProviderName} after {DurationMs}ms",
                model.ProviderName, stopwatch.ElapsedMilliseconds);
            throw new ImageGenerationException($"ComfyUI image generation failed: {ex.Message}", model.ProviderName, reasonCode: "client_error", inner: ex);
        }
    }

    /// <summary>
    /// Reference-conditioned generation (Qwen-Image-2.1 native multi-reference): uploads each
    /// reference to ComfyUI, then renders the scene FROM those references in one call.
    /// </summary>
    public async Task<byte[]> GenerateWithReferencesAsync(
        ResolvedImageModel model,
        ReferenceConditionedImageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        var baseUrl = (model.ComfyUiUrl ?? model.ProviderBaseUrl).TrimEnd('/');

        try
        {
            if (model.SceneImageModelFamily != SceneImageModelFamily.QwenImage21)
            {
                throw new ImageGenerationException(
                    $"Reference-conditioned generation is implemented for the Qwen-Image-2.1 family only, but model "
                    + $"'{model.ModelIdentifier}' is configured as '{model.SceneImageModelFamily}'.",
                    model.ProviderName,
                    reasonCode: "unsupported_reference_generation_family");
            }

            var qwenImage21 = model.QwenImage21
                ?? throw new ImageGenerationException(
                    $"Qwen-Image-2.1 model '{model.ModelIdentifier}' has no resolved text encoder / VAE / resolution "
                    + "qualification. Configure its NativeMultiReference qualification in Model Manager.",
                    model.ProviderName,
                    reasonCode: "missing_qwen_image_21_qualification");

            if (!LooksLikeFilename(model.ModelIdentifier))
            {
                throw new ImageGenerationException(
                    $"Model identifier '{model.ModelIdentifier}' is not a ComfyUI checkpoint filename. Configure the checkpoint filename as the model identifier in Model Manager.",
                    model.ProviderName,
                    reasonCode: "invalid_checkpoint_identifier");
            }

            if (request.References.Count == 0)
            {
                throw new ImageGenerationException(
                    "Reference-conditioned generation requires at least one reference image; a request with none must "
                    + "use the prompt-only path.",
                    model.ProviderName,
                    reasonCode: "no_reference_images");
            }

            if (request.References.Count > qwenImage21.MaxReferences)
            {
                throw new ImageGenerationException(
                    $"Qwen-Image-2.1 accepts at most {qwenImage21.MaxReferences} reference images, but the request "
                    + $"carries {request.References.Count}. Reduce the selection in the composer.",
                    model.ProviderName,
                    reasonCode: "too_many_references");
            }

            ValidatePromptMetadata(model);

            var client = _httpClientFactory.CreateClient("CompletionClient");
            client.Timeout = TimeSpan.FromSeconds(model.ProviderTimeoutSeconds);

            if (!string.IsNullOrEmpty(model.ApiKeyEncrypted))
            {
                var decryptedKey = _encryptionService.Decrypt(model.ApiKeyEncrypted);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", decryptedKey);
            }

            var uploadedNames = new List<string>(request.References.Count);
            foreach (var reference in request.References)
            {
                uploadedNames.Add(await UploadReferenceAsync(client, baseUrl, reference, model, cancellationToken));
            }

            var workflow = BuildQwenImage21Workflow(
                model.ModelIdentifier,
                qwenImage21,
                request.PositivePrompt,
                request.NegativePrompt,
                request.Size,
                request.Seed,
                uploadedNames);

            var payload = new JsonObject
            {
                ["prompt"] = workflow,
                ["client_id"] = "dreamgen-app"
            };

            _logger.LogInformation(
                "ComfyUI reference generation start: Provider={ProviderName}, Checkpoint={Checkpoint}, References={References}",
                model.ProviderName, model.ModelIdentifier, uploadedNames.Count);

            var bytes = await SubmitWorkflowAndFetchAsync(client, baseUrl, payload, model, request.PositivePrompt.Length, cancellationToken);
            stopwatch.Stop();
            return bytes;
        }
        catch (ImageGenerationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "ComfyUI reference generation failed for provider {ProviderName} after {DurationMs}ms",
                model.ProviderName, stopwatch.ElapsedMilliseconds);
            throw new ImageGenerationException($"ComfyUI reference generation failed: {ex.Message}", model.ProviderName, reasonCode: "client_error", inner: ex);
        }
    }

    private static async Task<string> UploadReferenceAsync(
        HttpClient client,
        string baseUrl,
        ReferenceConditionedImageInput reference,
        ResolvedImageModel model,
        CancellationToken cancellationToken)
    {
        if (reference.Content.Length == 0)
        {
            throw new ImageGenerationException(
                $"Reference '{reference.SemanticRole}' carries no image bytes.",
                model.ProviderName,
                reasonCode: "empty_reference_image");
        }

        // Shared with the editor and the upscaler so the upload contract has one implementation.
        await using var stream = new MemoryStream(reference.Content, writable: false);
        return await ComfyUiWorkflowTransport.UploadImageAsync(
            client, baseUrl, stream, reference.FileName, model.ProviderName, "comfyui", cancellationToken);
    }

    /// <summary>
    /// Submits an already-built prompt payload, waits for its history entry and returns the first
    /// output image. Single implementation for every ComfyUI family path (prompt-only and
    /// reference-conditioned) so the submit/poll/fetch contract and its reason codes cannot drift
    /// between them.
    /// </summary>
    private async Task<byte[]> SubmitWorkflowAndFetchAsync(
        HttpClient client,
        string baseUrl,
        JsonObject payload,
        ResolvedImageModel model,
        int promptChars,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ComfyUI image generation start: Provider={ProviderName}, Checkpoint={Checkpoint}, PromptChars={PromptChars}",
            model.ProviderName, model.ModelIdentifier, promptChars);

        using var submitResponse = await client.PostAsJsonAsync($"{baseUrl}/prompt", payload, cancellationToken);
        if (!submitResponse.IsSuccessStatusCode)
        {
            var errorContent = await submitResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new ImageGenerationException(
                $"ComfyUI prompt submission failed: {(int)submitResponse.StatusCode} {errorContent}",
                model.ProviderName, (int)submitResponse.StatusCode, "comfyui_submit_failed");
        }

        var submitBody = await submitResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        var promptId = submitBody?["prompt_id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(promptId))
        {
            throw new ImageGenerationException(
                "ComfyUI returned no prompt_id.", model.ProviderName, reasonCode: "comfyui_no_prompt_id");
        }

        // Poll /history/{promptId} until success/error or timeout.
        var deadline = DateTime.UtcNow.AddSeconds(model.ProviderTimeoutSeconds);
        JsonObject? historyEntry = null;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(2000, cancellationToken);
            using var histResponse = await client.GetAsync($"{baseUrl}/history/{promptId}", cancellationToken);
            if (histResponse.IsSuccessStatusCode)
            {
                var hist = await histResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
                if (hist is not null && hist.ContainsKey(promptId))
                {
                    historyEntry = hist[promptId] as JsonObject;
                    break;
                }
            }
        }

        if (historyEntry is null)
        {
            throw new ImageGenerationException(
                $"ComfyUI timed out waiting for prompt {promptId}.", model.ProviderName, reasonCode: "comfyui_timeout");
        }

        var status = historyEntry["status"]?["status_str"]?.GetValue<string>();
        if (status == "error")
        {
            throw new ImageGenerationException(
                $"ComfyUI workflow error for prompt {promptId}.", model.ProviderName, reasonCode: "comfyui_error");
        }
        if (status != "success")
        {
            throw new ImageGenerationException(
                $"ComfyUI unexpected status '{status}' for prompt {promptId}.", model.ProviderName, reasonCode: "comfyui_status");
        }

        // Extract first output image filename.
        string? filename = null;
        string? subfolder = null;
        string? type = null;
        if (historyEntry["outputs"] is JsonObject outputs)
        {
            foreach (var nodeOut in outputs)
            {
                if (nodeOut.Value is JsonObject nodeObj && nodeObj["images"] is JsonArray images)
                {
                    foreach (var imgNode in images)
                    {
                        if (imgNode is JsonObject img)
                        {
                            filename = img["filename"]?.GetValue<string>();
                            subfolder = img["subfolder"]?.GetValue<string>();
                            type = img["type"]?.GetValue<string>();
                            break;
                        }
                    }
                }
                if (filename is not null) break;
            }
        }

        if (string.IsNullOrEmpty(filename))
        {
            throw new ImageGenerationException(
                $"ComfyUI produced no output image for prompt {promptId}.", model.ProviderName, reasonCode: "comfyui_no_output");
        }

        var query = $"filename={Uri.EscapeDataString(filename)}";
        if (!string.IsNullOrEmpty(subfolder)) query += $"&subfolder={Uri.EscapeDataString(subfolder)}";
        if (!string.IsNullOrEmpty(type)) query += $"&type={Uri.EscapeDataString(type)}";

        using var viewResponse = await client.GetAsync($"{baseUrl}/view?{query}", cancellationToken);
        if (!viewResponse.IsSuccessStatusCode)
        {
            throw new ImageGenerationException(
                $"ComfyUI view failed: {(int)viewResponse.StatusCode}", model.ProviderName, (int)viewResponse.StatusCode, "comfyui_view_failed");
        }

        var bytes = await viewResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        _logger.LogInformation(
            "ComfyUI image generation completed: Provider={ProviderName}, Bytes={Bytes}",
            model.ProviderName, bytes.Length);
        return bytes;
    }

    public async Task<(bool Success, string Message)> CheckImageModelHealthAsync(
        string providerBaseUrl,
        string imageGenerationPath,
        int timeoutSeconds,
        string? decryptedApiKey,
        string modelIdentifier,
        ImageContentPolicy contentPolicy,
        CancellationToken cancellationToken = default,
        ImageProtocol imageProtocol = ImageProtocol.ComfyUi)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("CompletionClient");
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            if (!string.IsNullOrEmpty(decryptedApiKey))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", decryptedApiKey);
            }

            var baseUrl = providerBaseUrl.TrimEnd('/');
            using var response = await client.GetAsync($"{baseUrl}/system_stats", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return (true, "ComfyUI is reachable and responding.");
            }
            return (false, $"ComfyUI health check failed: HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, $"ComfyUI health check failed: {ex.Message}");
        }
    }

    private static bool LooksLikeFilename(string value)
        => value.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase)
           || value.EndsWith(".ckpt", StringComparison.OrdinalIgnoreCase);

    private static void ValidatePromptMetadata(ResolvedImageModel model)
    {
        if (!SceneImagePromptMetadata.IsCompatible(model.SceneImageModelFamily, model.PromptDialect))
        {
            throw new ImageGenerationException(
                $"Scene-image family '{model.SceneImageModelFamily}' is incompatible with prompt dialect '{model.PromptDialect}'. Configure both in Model Manager.",
                model.ProviderName,
                reasonCode: "invalid_image_prompt_metadata");
        }
    }
}
