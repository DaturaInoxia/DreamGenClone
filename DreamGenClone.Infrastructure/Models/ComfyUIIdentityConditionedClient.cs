using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.Models;

/// <summary>
/// ComfyUI identity-conditioned image client. Implements <see cref="IIdentityConditionedImageClient"/>
/// for the single-actor identity path: uploads the reference image, compiles the pinned IP-Adapter or
/// PuLID workflow, submits, polls, and returns the produced PNG. Separate from the prompt-only
/// <see cref="ComfyUIImageClient"/> — no silent fallback between them.
/// </summary>
public sealed class ComfyUIIdentityConditionedClient : IIdentityConditionedImageClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ComfyUIIdentityConditionedClient> _logger;

    public ComfyUIIdentityConditionedClient(
        IHttpClientFactory httpClientFactory,
        ILogger<ComfyUIIdentityConditionedClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<byte[]> GenerateAsync(
        ResolvedIdentityImageModel model,
        IdentityControlledImageRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var baseUrl = model.ProviderBaseUrl.TrimEnd('/');

        try
        {
            var client = _httpClientFactory.CreateClient("CompletionClient");
            client.Timeout = TimeSpan.FromSeconds(model.ProviderTimeoutSeconds);

            var workflow = await BuildWorkflowAsync(client, baseUrl, model, request, cancellationToken);

            var payload = new JsonObject
            {
                ["prompt"] = workflow,
                ["client_id"] = $"dreamgen-identity-{request.CorrelationId}"
            };

            _logger.LogInformation(
                "ComfyUI identity generation start: Provider={ProviderName}, Checkpoint={Checkpoint}, Mechanism={Mechanism}, References={ReferenceCount}",
                model.ProviderName, model.ModelIdentifier, model.Mechanism, request.References.Count > 1 ? request.References.Count : 1);

            using var submitResponse = await client.PostAsJsonAsync($"{baseUrl}/prompt", payload, cancellationToken);
            if (!submitResponse.IsSuccessStatusCode)
            {
                var errorContent = await submitResponse.Content.ReadAsStringAsync(cancellationToken);
                throw new ImageGenerationException(
                    $"ComfyUI identity prompt submission failed: {(int)submitResponse.StatusCode} {errorContent}",
                    model.ProviderName, (int)submitResponse.StatusCode, "comfyui_identity_submit_failed");
            }

            var submitBody = await submitResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
            var promptId = submitBody?["prompt_id"]?.GetValue<string>();
            if (string.IsNullOrEmpty(promptId))
            {
                throw new ImageGenerationException(
                    "ComfyUI returned no prompt_id for identity render.", model.ProviderName, reasonCode: "comfyui_no_prompt_id");
            }

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
                    $"ComfyUI timed out waiting for identity prompt {promptId}.", model.ProviderName, reasonCode: "comfyui_timeout");
            }

            var status = historyEntry["status"]?["status_str"]?.GetValue<string>();
            if (status != "success")
            {
                throw new ImageGenerationException(
                    $"ComfyUI identity workflow '{status}' for prompt {promptId}.", model.ProviderName, reasonCode: "comfyui_identity_error");
            }

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
                    $"ComfyUI produced no output image for identity prompt {promptId}.", model.ProviderName, reasonCode: "comfyui_no_output");
            }

            var query = $"filename={Uri.EscapeDataString(filename)}";
            if (!string.IsNullOrEmpty(subfolder)) query += $"&subfolder={Uri.EscapeDataString(subfolder)}";
            if (!string.IsNullOrEmpty(type)) query += $"&type={Uri.EscapeDataString(type)}";

            using var viewResponse = await client.GetAsync($"{baseUrl}/view?{query}", cancellationToken);
            if (!viewResponse.IsSuccessStatusCode)
            {
                throw new ImageGenerationException(
                    $"ComfyUI identity view failed: {(int)viewResponse.StatusCode}", model.ProviderName, (int)viewResponse.StatusCode, "comfyui_view_failed");
            }

            var bytes = await viewResponse.Content.ReadAsByteArrayAsync(cancellationToken);
            stopwatch.Stop();
            _logger.LogInformation(
                "ComfyUI identity generation completed: Provider={ProviderName}, Bytes={Bytes}, DurationMs={DurationMs}",
                model.ProviderName, bytes.Length, stopwatch.ElapsedMilliseconds);
            return bytes;
        }
        catch (ImageGenerationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "ComfyUI identity generation failed for provider {ProviderName} after {DurationMs}ms",
                model.ProviderName, stopwatch.ElapsedMilliseconds);
            throw new ImageGenerationException($"ComfyUI identity generation failed: {ex.Message}", model.ProviderName, reasonCode: "client_error", inner: ex);
        }
    }

    private async Task<string> UploadReferenceImageAsync(
        HttpClient client,
        string baseUrl,
        byte[] imageBytes,
        string nameStem,
        CancellationToken cancellationToken)
    {
        var referenceName = $"{nameStem}.png";

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(imageBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "image", referenceName);

        using var uploadResponse = await client.PostAsync($"{baseUrl}/upload/image", content, cancellationToken);
        if (!uploadResponse.IsSuccessStatusCode)
        {
            var errorContent = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new ImageGenerationException(
                $"ComfyUI reference image upload failed: {(int)uploadResponse.StatusCode} {errorContent}",
                "ComfyUI", (int)uploadResponse.StatusCode, "comfyui_upload_failed");
        }

        var uploadBody = await uploadResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        var name = uploadBody?["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(name))
        {
            throw new ImageGenerationException(
                "ComfyUI returned no name for the uploaded reference image.", "ComfyUI", reasonCode: "comfyui_upload_no_name");
        }

        _logger.LogDebug("Uploaded identity reference image {Name}", name);
        return name;
    }

    internal static JsonObject BuildIpAdapterWorkflow(
        string checkpointName,
        string preset,
        string referenceName,
        IdentityControlledImageRequest request,
        double strength)
    {
        var (width, height) = ParseSize(request.Size);
        return new JsonObject
        {
            ["4"] = new JsonObject
            {
                ["class_type"] = "CheckpointLoaderSimple",
                ["inputs"] = new JsonObject { ["ckpt_name"] = checkpointName }
            },
            ["6"] = new JsonObject
            {
                ["class_type"] = "CLIPTextEncode",
                ["inputs"] = new JsonObject { ["text"] = request.PositivePrompt, ["clip"] = new JsonArray("4", 1) }
            },
            ["7"] = new JsonObject
            {
                ["class_type"] = "CLIPTextEncode",
                ["inputs"] = new JsonObject { ["text"] = request.NegativePrompt, ["clip"] = new JsonArray("4", 1) }
            },
            ["5"] = new JsonObject
            {
                ["class_type"] = "EmptyLatentImage",
                ["inputs"] = new JsonObject { ["width"] = width, ["height"] = height, ["batch_size"] = 1 }
            },
            ["10"] = new JsonObject
            {
                ["class_type"] = "IPAdapterUnifiedLoader",
                ["inputs"] = new JsonObject { ["model"] = new JsonArray("4", 0), ["preset"] = preset }
            },
            ["11"] = new JsonObject
            {
                ["class_type"] = "LoadImage",
                ["inputs"] = new JsonObject { ["image"] = referenceName }
            },
            ["12"] = new JsonObject
            {
                ["class_type"] = "IPAdapter",
                ["inputs"] = new JsonObject
                {
                    ["model"] = new JsonArray("10", 0),
                    ["ipadapter"] = new JsonArray("10", 1),
                    ["image"] = new JsonArray("11", 0),
                    ["weight"] = strength,
                    ["start_at"] = 0.0,
                    ["end_at"] = 1.0,
                    ["weight_type"] = "standard"
                }
            },
            ["3"] = BuildKSampler("12", request.Seed),
            ["8"] = new JsonObject
            {
                ["class_type"] = "VAEDecode",
                ["inputs"] = new JsonObject { ["samples"] = new JsonArray("3", 0), ["vae"] = new JsonArray("4", 2) }
            },
            ["9"] = new JsonObject
            {
                ["class_type"] = "SaveImage",
                ["inputs"] = new JsonObject { ["filename_prefix"] = "dreamgen_identity", ["images"] = new JsonArray("8", 0) }
            }
        };
    }

    internal static JsonObject BuildPuLidWorkflow(
        string checkpointName,
        string pulidFile,
        string referenceName,
        IdentityControlledImageRequest request,
        double strength)
    {
        var (width, height) = ParseSize(request.Size);
        return new JsonObject
        {
            ["4"] = new JsonObject
            {
                ["class_type"] = "CheckpointLoaderSimple",
                ["inputs"] = new JsonObject { ["ckpt_name"] = checkpointName }
            },
            ["6"] = new JsonObject
            {
                ["class_type"] = "CLIPTextEncode",
                ["inputs"] = new JsonObject { ["text"] = request.PositivePrompt, ["clip"] = new JsonArray("4", 1) }
            },
            ["7"] = new JsonObject
            {
                ["class_type"] = "CLIPTextEncode",
                ["inputs"] = new JsonObject { ["text"] = request.NegativePrompt, ["clip"] = new JsonArray("4", 1) }
            },
            ["5"] = new JsonObject
            {
                ["class_type"] = "EmptyLatentImage",
                ["inputs"] = new JsonObject { ["width"] = width, ["height"] = height, ["batch_size"] = 1 }
            },
            ["10"] = new JsonObject
            {
                ["class_type"] = "PulidModelLoader",
                ["inputs"] = new JsonObject { ["pulid_file"] = pulidFile }
            },
            ["11"] = new JsonObject
            {
                ["class_type"] = "LoadImage",
                ["inputs"] = new JsonObject { ["image"] = referenceName }
            },
            ["13"] = new JsonObject
            {
                ["class_type"] = "PulidInsightFaceLoader",
                ["inputs"] = new JsonObject { ["provider"] = "CPU" }
            },
            ["14"] = new JsonObject
            {
                ["class_type"] = "PulidEvaClipLoader",
                ["inputs"] = new JsonObject()
            },
            ["12"] = new JsonObject
            {
                ["class_type"] = "ApplyPulid",
                ["inputs"] = new JsonObject
                {
                    ["model"] = new JsonArray("4", 0),
                    ["pulid"] = new JsonArray("10", 0),
                    ["eva_clip"] = new JsonArray("14", 0),
                    ["face_analysis"] = new JsonArray("13", 0),
                    ["image"] = new JsonArray("11", 0),
                    ["method"] = "fidelity",
                    ["weight"] = strength,
                    ["start_at"] = 0.0,
                    ["end_at"] = 1.0
                }
            },
            ["3"] = BuildKSampler("12", request.Seed),
            ["8"] = new JsonObject
            {
                ["class_type"] = "VAEDecode",
                ["inputs"] = new JsonObject { ["samples"] = new JsonArray("3", 0), ["vae"] = new JsonArray("4", 2) }
            },
            ["9"] = new JsonObject
            {
                ["class_type"] = "SaveImage",
                ["inputs"] = new JsonObject { ["filename_prefix"] = "dreamgen_identity", ["images"] = new JsonArray("8", 0) }
            }
        };
    }

    private async Task<JsonObject> BuildWorkflowAsync(
        HttpClient client,
        string baseUrl,
        ResolvedIdentityImageModel model,
        IdentityControlledImageRequest request,
        CancellationToken cancellationToken)
    {
        var isMulti = request.References.Count > 1;
        if (isMulti && model.Mechanism != SceneImageIdentityMechanism.IpAdapter)
        {
            throw new ImageGenerationException(
                $"Identity mechanism '{model.Mechanism}' does not support multi-character rendering; use IP-Adapter.",
                model.ProviderName, reasonCode: "unsupported_identity_mechanism_multi");
        }

        if (isMulti)
        {
            var (width, height) = ParseSize(request.Size);
            var uploaded = new List<(string ReferenceName, string MaskName, IdentityReferenceInput Reference)>();
            for (var i = 0; i < request.References.Count; i++)
            {
                var reference = request.References[i];
                var refName = await UploadReferenceImageAsync(
                    client, baseUrl, reference.ReferenceImageBytes, $"{request.CorrelationId}-ref{i}", cancellationToken);
                var maskBytes = reference.MaskBytes ?? SynthesizeBandMask(width, height, i, request.References.Count);
                var maskName = await UploadReferenceImageAsync(
                    client, baseUrl, maskBytes, $"{request.CorrelationId}-mask{i}", cancellationToken);
                uploaded.Add((refName, maskName, reference));
            }

            return BuildMultiIpAdapterWorkflow(model.ModelIdentifier, model.AdapterRef, uploaded, request, model.IdentityStrength);
        }

        var referenceBytes = request.References.Count == 1
            ? request.References[0].ReferenceImageBytes
            : request.ReferenceImageBytes;
        var referenceName = await UploadReferenceImageAsync(
            client, baseUrl, referenceBytes, $"identity-ref-{request.CorrelationId}", cancellationToken);

        return model.Mechanism switch
        {
            SceneImageIdentityMechanism.IpAdapter => BuildIpAdapterWorkflow(
                model.ModelIdentifier, model.AdapterRef, referenceName, request, model.IdentityStrength),
            SceneImageIdentityMechanism.PuLid => BuildPuLidWorkflow(
                model.ModelIdentifier, model.AdapterRef, referenceName, request, model.IdentityStrength),
            _ => throw new ImageGenerationException(
                $"Unsupported identity mechanism '{model.Mechanism}'.", model.ProviderName, reasonCode: "unsupported_identity_mechanism")
        };
    }

    /// <summary>
    /// Builds the multi-character IP-Adapter workflow proven by the two-character proof harness: one
    /// LoadImage + LoadImageMask + chained IPAdapter node per character, each with its own weight and
    /// regional attention mask. The KSampler is wired from the final chained IPAdapter node.
    /// </summary>
    internal static JsonObject BuildMultiIpAdapterWorkflow(
        string checkpointName,
        string preset,
        IReadOnlyList<(string ReferenceName, string MaskName, IdentityReferenceInput Reference)> references,
        IdentityControlledImageRequest request,
        double defaultStrength)
    {
        if (references.Count < 2)
        {
            throw new ArgumentException("Multi-character workflow requires at least two references.", nameof(references));
        }

        var (width, height) = ParseSize(request.Size);
        var workflow = new JsonObject
        {
            ["4"] = new JsonObject
            {
                ["class_type"] = "CheckpointLoaderSimple",
                ["inputs"] = new JsonObject { ["ckpt_name"] = checkpointName }
            },
            ["6"] = new JsonObject
            {
                ["class_type"] = "CLIPTextEncode",
                ["inputs"] = new JsonObject { ["text"] = request.PositivePrompt, ["clip"] = new JsonArray("4", 1) }
            },
            ["7"] = new JsonObject
            {
                ["class_type"] = "CLIPTextEncode",
                ["inputs"] = new JsonObject { ["text"] = request.NegativePrompt, ["clip"] = new JsonArray("4", 1) }
            },
            ["5"] = new JsonObject
            {
                ["class_type"] = "EmptyLatentImage",
                ["inputs"] = new JsonObject { ["width"] = width, ["height"] = height, ["batch_size"] = 1 }
            },
            ["10"] = new JsonObject
            {
                ["class_type"] = "IPAdapterUnifiedLoader",
                ["inputs"] = new JsonObject { ["model"] = new JsonArray("4", 0), ["preset"] = preset }
            }
        };

        string previousModelNode = "10";
        for (var i = 0; i < references.Count; i++)
        {
            var loadImageId = (11 + i * 2).ToString();
            var loadMaskId = (12 + i * 2).ToString();
            var ipNodeId = (20 + i).ToString();
            var (refName, maskName, reference) = references[i];
            var strength = reference.StrengthOverride ?? defaultStrength;

            workflow[loadImageId] = new JsonObject
            {
                ["class_type"] = "LoadImage",
                ["inputs"] = new JsonObject { ["image"] = refName }
            };
            workflow[loadMaskId] = new JsonObject
            {
                ["class_type"] = "LoadImageMask",
                ["inputs"] = new JsonObject { ["image"] = maskName, ["channel"] = "red" }
            };
            workflow[ipNodeId] = new JsonObject
            {
                ["class_type"] = "IPAdapter",
                ["inputs"] = new JsonObject
                {
                    ["model"] = new JsonArray(previousModelNode, 0),
                    ["ipadapter"] = new JsonArray("10", 1),
                    ["image"] = new JsonArray(loadImageId, 0),
                    ["weight"] = strength,
                    ["start_at"] = 0.0,
                    ["end_at"] = 1.0,
                    ["weight_type"] = "standard",
                    ["attn_mask"] = new JsonArray(loadMaskId, 0)
                }
            };
            previousModelNode = ipNodeId;
        }

        workflow["3"] = BuildKSampler(previousModelNode, request.Seed);
        workflow["8"] = new JsonObject
        {
            ["class_type"] = "VAEDecode",
            ["inputs"] = new JsonObject { ["samples"] = new JsonArray("3", 0), ["vae"] = new JsonArray("4", 2) }
        };
        workflow["9"] = new JsonObject
        {
            ["class_type"] = "SaveImage",
            ["inputs"] = new JsonObject { ["filename_prefix"] = "dreamgen_identity", ["images"] = new JsonArray("8", 0) }
        };
        return workflow;
    }

    /// <summary>
    /// Synthesizes a default regional mask (white = conditioned region) for one character in a
    /// multi-character render: character <paramref name="index"/> of <paramref name="count"/> gets a
    /// vertical band. Returns a grayscale PNG suitable for ComfyUI LoadImageMask.
    /// </summary>
    internal static byte[] SynthesizeBandMask(int width, int height, int index, int count)
    {
        if (count < 2) throw new ArgumentOutOfRangeException(nameof(count), "At least two characters are required for a band mask.");
        if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));

        var startX = (int)((long)index * width / count);
        var endX = (int)((long)(index + 1) * width / count);

        var stride = 1 + width;
        var raw = new byte[height * stride];
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            raw[row] = 0; // filter: None
            for (var x = 0; x < width; x++)
            {
                raw[row + 1 + x] = (x >= startX && x < endX) ? (byte)255 : (byte)0;
            }
        }

        return EncodeGrayPng(raw, width, height);
    }

    private static byte[] EncodeGrayPng(byte[] rawWithFilterBytes, int width, int height)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 0;  // color type: grayscale
        ihdr[10] = 0; // compression
        ihdr[11] = 0; // filter
        ihdr[12] = 0; // interlace
        WriteChunk(output, "IHDR", ihdr);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(rawWithFilterBytes, 0, rawWithFilterBytes.Length);
        }
        WriteChunk(output, "IDAT", compressed.ToArray());

        WriteChunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return crc ^ 0xFFFFFFFFu;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        var crcInput = new byte[typeBytes.Length + data.Length];
        typeBytes.CopyTo(crcInput, 0);
        data.CopyTo(crcInput, typeBytes.Length);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(crcInput));
        stream.Write(crc);
    }

    private static JsonObject BuildKSampler(string modelNodeId, long? seed) => new()
    {
        ["class_type"] = "KSampler",
        ["inputs"] = new JsonObject
        {
            ["seed"] = seed ?? Random.Shared.Next(0, int.MaxValue),
            ["steps"] = 30,
            ["cfg"] = 5.0,
            ["sampler_name"] = "dpmpp_2m_sde",
            ["scheduler"] = "karras",
            ["denoise"] = 1.0,
            ["model"] = new JsonArray(modelNodeId, 0),
            ["positive"] = new JsonArray("6", 0),
            ["negative"] = new JsonArray("7", 0),
            ["latent_image"] = new JsonArray("5", 0)
        }
    };

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
}
