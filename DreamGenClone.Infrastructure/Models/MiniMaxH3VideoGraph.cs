using System.Globalization;
using System.Text.Json.Nodes;
using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Infrastructure.Models;

/// <summary>
/// Builds the MiniMax H3 (Ref2VA) ComfyUI graph in API format from configured artifacts, the compiled prompt,
/// the ordered reference images and the operator's sampling values.
/// </summary>
/// <remarks>
/// <para>
/// The graph is the shape verified on the host in the B-150 sweep, with the switch/math convenience nodes of the
/// official template replaced by their fixed values (a literal frame count, and the LoRA chain wired straight
/// into the guider) so the emitted graph is deterministic and carries no node the render does not need.
/// </para>
/// <para>
/// Three invariants are enforced here rather than trusted, because violating any of them fails SILENTLY on the
/// host:
/// <list type="bullet">
/// <item>the audio half of the graph (<c>VAEDecodeAudio</c> + <c>CreateVideo.audio</c>) is always emitted - a
/// graph without it produces a mute file that looks like a successful render;</item>
/// <item>reference images cannot be bound without the video VAE, and the audio VAE is always required - the node
/// marks both inputs optional, and omitting them degrades reference conditioning to text-encoder-only instead of
/// failing;</item>
/// <item>the frame count must be a value the node accepts, checked against the configured bounds before a
/// ~25 minute submission.</item>
/// </list>
/// </para>
/// </remarks>
internal static class MiniMaxH3VideoGraph
{
    /// <summary>Node id of the <c>MiniMaxH3ReferenceToVideo</c> node (its outputs are [0] conditioning, [1] latent).</summary>
    internal const string ReferenceNode = "10";

    private const string UnetNode = "1";
    private const string ClipNode = "2";
    private const string VideoVaeNode = "3";
    private const string AudioVaeNode = "4";
    private const string NoiseNode = "11";
    private const string SamplerSelectNode = "12";
    private const string SchedulerNode = "13";
    private const string GuiderNode = "14";
    private const string SamplerNode = "15";
    private const string VideoDecodeNode = "16";
    private const string AudioDecodeNode = "17";
    private const string CreateVideoNode = "18";
    private const string SaveVideoNode = "19";
    private const int FirstReferenceImageNode = 30;
    private const int FirstLoraNode = 100;

    /// <summary>Node id of the continuation guide frame's <c>LoadImage</c> (B-156).</summary>
    private const string GuideImageNode = "40";

    /// <summary>Node id of the continuation guide's <c>LoadAudio</c> (B-156).</summary>
    private const string GuideAudioNode = "42";

    /// <summary>
    /// Node id of the <c>MiniMaxH3AddGuide</c> node (B-156). It consumes the reference node's conditioning AND
    /// latent and returns conditioning, so the guider reads it instead when a continuation is present.
    /// </summary>
    internal const string GuideNode = "41";

    internal static JsonObject Build(
        MiniMaxH3Refs refs,
        string prompt,
        IReadOnlyList<string> referenceImageNames,
        int width,
        int height,
        int length,
        int steps,
        long seed,
        string refImageSize,
        string outputPrefix,
        IReadOnlyList<ResolvedSceneLora>? loras,
        string? guideImageName = null,
        string? guideAudioName = null,
        int guideFrameIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(refs);
        ArgumentNullException.ThrowIfNull(referenceImageNames);

        RequireArtifact(refs.DitName, "DitName");
        RequireArtifact(refs.TextEncoderName, "TextEncoderName");
        RequireArtifact(refs.TextEncoderDevice, "TextEncoderDevice");
        RequireArtifact(refs.SamplerName, "SamplerName");
        RequireArtifact(refs.Scheduler, "Scheduler");
        RequireArtifact(outputPrefix, nameof(outputPrefix));

        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new InvalidOperationException(
                "The MiniMax H3 graph needs the compiled prompt; an empty prompt renders an unconditioned clip.");
        }

        if (steps < 1)
        {
            throw new InvalidOperationException(
                $"MiniMax H3 steps must be at least 1, but {steps} was requested.");
        }

        if (!refs.FramePolicy.IsAccepted(length))
        {
            throw new InvalidOperationException(
                $"MiniMax H3 length {length} is not a value the node accepts. It must be between "
                + $"{refs.FramePolicy.MinFrames} and {refs.FramePolicy.MaxFrames} in steps of "
                + $"{refs.FramePolicy.FrameStep} from {refs.FramePolicy.MinFrames} "
                + $"(for example {refs.FramePolicy.PresetShortFrames} or {refs.FramePolicy.PresetLongFrames}). "
                + "Nothing was submitted.");
        }

        RequireCanvasDimension(width, "width");
        RequireCanvasDimension(height, "height");

        if (refImageSize is not ("match" or "max"))
        {
            throw new InvalidOperationException(
                $"MiniMax H3 ref_image_size must be 'match' or 'max', but '{refImageSize}' was requested. "
                + "The node accepts no other value.");
        }

        if (referenceImageNames.Count > refs.MaxReferenceImages)
        {
            throw new InvalidOperationException(
                $"MiniMax H3 accepts at most {refs.MaxReferenceImages} reference images, but "
                + $"{referenceImageNames.Count} were supplied. Reduce the reference list.");
        }

        // The VAEs are marked optional by the node, so a missing one does not fail on the host - it silently
        // drops reference conditioning to the text encoder. Both are required here instead.
        if (referenceImageNames.Count > 0 && string.IsNullOrWhiteSpace(refs.VideoVaeName))
        {
            throw new InvalidOperationException(
                "MiniMax H3 cannot bind reference images without its video VAE ('VideoVaeName'): the node marks "
                + "'vae' optional and would quietly condition on the text encoder only. Configure 'VideoVaeName' "
                + "on the model.");
        }

        RequireVae(refs.VideoVaeName, "VideoVaeName");
        RequireVae(refs.AudioVaeName, "AudioVaeName");

        foreach (var name in referenceImageNames)
        {
            RequireArtifact(name, "reference image file name");
        }

        var graph = new JsonObject
        {
            [UnetNode] = Node("UNETLoader", new JsonObject
            {
                ["unet_name"] = refs.DitName,
                ["weight_dtype"] = "default"
            }),
            [ClipNode] = Node("CLIPLoader", new JsonObject
            {
                ["clip_name"] = refs.TextEncoderName,
                ["type"] = "minimax",
                ["device"] = refs.TextEncoderDevice
            }),
            [VideoVaeNode] = Node("VAELoader", new JsonObject { ["vae_name"] = refs.VideoVaeName }),
            [AudioVaeNode] = Node("VAELoader", new JsonObject { ["vae_name"] = refs.AudioVaeName })
        };

        // The LoRA chain hangs off the UNETLoader; the guider and the scheduler read its tail. With no LoRA the
        // chain is the loader itself, so a render with no selection is the untouched base model.
        var modelTail = UnetNode;
        var nextLoraNode = FirstLoraNode;
        foreach (var lora in loras ?? [])
        {
            if (string.IsNullOrWhiteSpace(lora.FileName))
            {
                throw new InvalidOperationException(
                    "A LoRA in the stack has no file name; remove the empty entry rather than rendering with it.");
            }

            if (lora.Strength <= 0 || !double.IsFinite(lora.Strength))
            {
                throw new InvalidOperationException(
                    $"LoRA '{lora.FileName}' has strength {lora.Strength.ToString(CultureInfo.InvariantCulture)}, "
                    + "which is not a positive number. The strength the operator chose is required.");
            }

            var nodeId = nextLoraNode++.ToString(CultureInfo.InvariantCulture);
            graph[nodeId] = Node("LoraLoaderModelOnly", new JsonObject
            {
                ["lora_name"] = lora.FileName,
                ["strength_model"] = lora.Strength,
                ["model"] = new JsonArray(modelTail, 0)
            });
            modelTail = nodeId;
        }

        // Reference images: slot order is the <Picture i> numbering, so LoadImage nodes are emitted in the
        // operator's order and bound to ref_image_<i> in that same order.
        var referenceInputs = new JsonObject();
        for (var index = 0; index < referenceImageNames.Count; index++)
        {
            var nodeId = (FirstReferenceImageNode + index).ToString(CultureInfo.InvariantCulture);
            graph[nodeId] = Node("LoadImage", new JsonObject
            {
                ["image"] = referenceImageNames[index],
                ["upload"] = "image"
            });
            referenceInputs[$"ref_images.ref_image_{index}"] = new JsonArray(nodeId, 0);
        }

        var referenceInputsObject = new JsonObject
        {
            ["clip"] = new JsonArray(ClipNode, 0),
            ["prompt"] = prompt,
            ["width"] = width,
            ["height"] = height,
            ["length"] = length,
            ["ref_image_size"] = refImageSize,
            ["vae"] = new JsonArray(VideoVaeNode, 0),
            ["audio_vae"] = new JsonArray(AudioVaeNode, 0)
        };
        foreach (var input in referenceInputs)
        {
            referenceInputsObject[input.Key] = input.Value!.DeepClone();
        }

        graph[ReferenceNode] = Node("MiniMaxH3ReferenceToVideo", referenceInputsObject);

        // B-156 continuation: one extra node pins the source clip's final frame (and optionally its audio) at a
        // chosen frame index of THIS clip, in the conditioning. When it is present the guider reads ITS output
        // instead of the reference node's - and when a continuation was requested but something is missing, this
        // FAILS rather than emitting a guide-less graph that would look like a continuous render.
        var guiderConditioning = new JsonArray(ReferenceNode, 0);
        if (!string.IsNullOrWhiteSpace(guideImageName))
        {
            if (guideFrameIndex < 0 || guideFrameIndex >= length)
            {
                throw new InvalidOperationException(
                    $"The continuation guide pins frame {guideFrameIndex}, which is outside this clip's {length} "
                    + "frames. Fix the guide frame index rather than submitting a render the anchor cannot affect.");
            }

            graph[GuideImageNode] = Node("LoadImage", new JsonObject
            {
                ["image"] = guideImageName,
                ["upload"] = "image"
            });

            var guideInputs = new JsonObject
            {
                ["positive"] = new JsonArray(ReferenceNode, 0),
                ["latent"] = new JsonArray(ReferenceNode, 1),
                ["frame_idx"] = guideFrameIndex,
                ["vae"] = new JsonArray(VideoVaeNode, 0),
                ["audio_vae"] = new JsonArray(AudioVaeNode, 0),
                ["image"] = new JsonArray(GuideImageNode, 0)
            };

            if (!string.IsNullOrWhiteSpace(guideAudioName))
            {
                graph[GuideAudioNode] = Node("LoadAudio", new JsonObject { ["audio"] = guideAudioName });
                guideInputs["audio"] = new JsonArray(GuideAudioNode, 0);
            }

            graph[GuideNode] = Node("MiniMaxH3AddGuide", guideInputs);
            guiderConditioning = new JsonArray(GuideNode, 0);
        }
        else if (!string.IsNullOrWhiteSpace(guideAudioName))
        {
            throw new InvalidOperationException(
                "A continuation cannot carry the source audio without also carrying its final frame: the guide's "
                + "audio input only exists on the same node as the frame. Nothing was submitted.");
        }

        graph[NoiseNode] = Node("RandomNoise", new JsonObject
        {
            ["noise_seed"] = seed,
            ["control_after_generate"] = "fixed"
        });
        graph[SamplerSelectNode] = Node("KSamplerSelect", new JsonObject { ["sampler_name"] = refs.SamplerName });
        graph[SchedulerNode] = Node("BasicScheduler", new JsonObject
        {
            ["scheduler"] = refs.Scheduler,
            ["steps"] = steps,
            ["denoise"] = refs.Denoise,
            ["model"] = new JsonArray(modelTail, 0)
        });
        graph[GuiderNode] = Node("BasicGuider", new JsonObject
        {
            ["model"] = new JsonArray(modelTail, 0),
            ["conditioning"] = guiderConditioning
        });
        graph[SamplerNode] = Node("SamplerCustomAdvanced", new JsonObject
        {
            ["noise"] = new JsonArray(NoiseNode, 0),
            ["guider"] = new JsonArray(GuiderNode, 0),
            ["sampler"] = new JsonArray(SamplerSelectNode, 0),
            ["sigmas"] = new JsonArray(SchedulerNode, 0),
            ["latent_image"] = new JsonArray(ReferenceNode, 1)
        });
        graph[VideoDecodeNode] = Node("VAEDecode", new JsonObject
        {
            ["samples"] = new JsonArray(SamplerNode, 0),
            ["vae"] = new JsonArray(VideoVaeNode, 0)
        });
        graph[AudioDecodeNode] = Node("VAEDecodeAudio", new JsonObject
        {
            ["samples"] = new JsonArray(SamplerNode, 0),
            ["vae"] = new JsonArray(AudioVaeNode, 0)
        });
        graph[CreateVideoNode] = Node("CreateVideo", new JsonObject
        {
            ["fps"] = refs.Fps,
            ["bit_depth"] = refs.BitDepth,
            ["images"] = new JsonArray(VideoDecodeNode, 0),
            ["audio"] = new JsonArray(AudioDecodeNode, 0)
        });
        graph[SaveVideoNode] = Node("SaveVideo", new JsonObject
        {
            ["filename_prefix"] = outputPrefix,
            ["format"] = "auto",
            ["codec"] = "auto",
            ["video"] = new JsonArray(CreateVideoNode, 0)
        });

        return graph;
    }

    /// <summary>The frame lengths the node accepts, from the configured policy. Used by the composer's dropdown.</summary>
    internal static IReadOnlyList<int> AcceptedFrameLengths(MiniMaxH3FramePolicy policy)
    {
        if (policy.FrameStep <= 0)
        {
            throw new InvalidOperationException(
                $"MiniMax H3 FrameStep must be positive, but {policy.FrameStep} is configured.");
        }

        var lengths = new List<int>();
        for (var frames = policy.MinFrames; frames <= policy.MaxFrames; frames += policy.FrameStep)
        {
            lengths.Add(frames);
        }
        return lengths;
    }

    private static JsonObject Node(string classType, JsonObject inputs) => new()
    {
        ["class_type"] = classType,
        ["inputs"] = inputs
    };

    private static void RequireArtifact(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"MiniMax H3 graph configuration is missing '{field}'. Set it on the video model in Model Manager; "
                + "no artifact name is guessed.");
        }
    }

    private static void RequireVae(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"MiniMax H3 requires '{field}'. The node marks its VAE inputs optional, so omitting one degrades "
                + "reference conditioning to the text encoder instead of failing; configure it on the video model.");
        }
    }

    private static void RequireCanvasDimension(int value, string field)
    {
        if (value <= 0 || value % 32 != 0)
        {
            throw new InvalidOperationException(
                $"MiniMax H3 {field} must be a positive multiple of 32 (the node's step), but {value} was "
                + "requested.");
        }
    }
}
