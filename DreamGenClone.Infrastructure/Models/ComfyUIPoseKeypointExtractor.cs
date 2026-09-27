using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.Models;

/// <summary>
/// Reads a pose out of an image using the ComfyUI host's own pose estimator, over the SAME transport as every other
/// workflow client (upload, submit, wait for history) so extraction is a third caller of one conversation rather than
/// a third copy of it.
///
/// Two facts about the wire format are measured rather than assumed (2026-09-26, local host, 1024x1536 plate):
///
/// - The document is a LIST whose first element carries <c>canvas_width</c>/<c>canvas_height</c> beside
///   <c>people</c>. Reading the outer list as the person yields an empty joint list, which reads exactly like "no
///   figure was found" and sends you off tuning something that is not wrong.
/// - The keypoints are FRACTIONS of that canvas: the nose came back as <c>0.449, 0.111</c>, and the whole body spans
///   0..0.638 by 0..0.861. The app stores keypoints in SOURCE-IMAGE PIXELS (the pack's own JSON holds 373.9 on a
///   512x768 canvas) and its renderer and rig-fit both read them that way, so this scales by the canvas it was given.
///   Storing the fractions unscaled would produce a stamp-sized skeleton that fits nothing, while looking like a
///   successful extraction.
///
/// The node is <see cref="NodeName"/>. A <c>DWPreprocessor</c> node does not exist on this server, which is the name
/// the plan was written against; naming the node that is actually there is why the failure message when it is absent
/// says which node it looked for.
/// </summary>
public sealed class ComfyUIPoseKeypointExtractor : IPoseKeypointExtractor
{
    /// <summary>The estimator node this server exposes. Named in the refusal when a server does not have it.</summary>
    public const string NodeName = "OpenposePreprocessor";

    /// <summary>Which version of the graph below produced a keypoint set, recorded in the pose's provenance.</summary>
    public const string WorkflowVersion = "pose-extract/1";

    private const string ReasonPrefix = "comfyui_pose_extract";
    private const int BodyValues = 18 * 3;
    private const int HandValues = 21 * 3;
    private const int FaceValues = 70 * 3;

    private static readonly string NodeSignature =
        "detect_body=enable;detect_face=enable;detect_hand=enable;resolution=1024";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ComfyUIPoseKeypointExtractor> _logger;

    public ComfyUIPoseKeypointExtractor(
        IHttpClientFactory httpClientFactory, ILogger<ComfyUIPoseKeypointExtractor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<PoseKeypointExtractionResult> ExtractAsync(
        PoseKeypointExtractionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Image.Length == 0)
            throw new InvalidOperationException("A pose cannot be extracted from an empty image.");

        if (string.IsNullOrWhiteSpace(request.BaseUrl))
        {
            throw new InvalidOperationException(
                "The image model that would extract the pose has no ComfyUI base URL, so there is nowhere to send the "
                + "image. Set the model's ComfyUI URL in Model Manager.");
        }

        if (request.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"The image model's provider timeout is {request.TimeoutSeconds}s, which cannot run an extraction. "
                + "Set a positive timeout in Model Manager.");
        }

        var baseUrl = request.BaseUrl.TrimEnd('/');
        var provider = string.IsNullOrWhiteSpace(request.ProviderName) ? "ComfyUI" : request.ProviderName;

        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(request.TimeoutSeconds);

        // A unique upload name on purpose: ComfyUI caches execution by graph and inputs, and a cache hit comes back
        // through /history WITHOUT the openpose_json output — which reads as "the estimator returned no keypoints".
        var fileName = $"pose-extract-{Guid.NewGuid():N}.png";
        using var image = new MemoryStream(request.Image, writable: false);
        var storedName = await ComfyUiWorkflowTransport.UploadImageAsync(
            client, baseUrl, image, fileName, provider, ReasonPrefix, cancellationToken);

        var graph = new JsonObject
        {
            ["1"] = new JsonObject
            {
                ["class_type"] = "LoadImage",
                ["inputs"] = new JsonObject { ["image"] = storedName }
            },
            ["2"] = new JsonObject
            {
                ["class_type"] = NodeName,
                ["inputs"] = new JsonObject
                {
                    ["image"] = new JsonArray("1", 0),
                    ["detect_body"] = "enable",
                    ["detect_face"] = "enable",
                    ["detect_hand"] = "enable",
                    ["resolution"] = 1024
                }
            },
            // POSE_KEYPOINT is not a terminal output, so the graph needs an image sink or ComfyUI rejects the whole
            // prompt with prompt_no_outputs and nothing is measured.
            ["3"] = new JsonObject
            {
                ["class_type"] = "SaveImage",
                ["inputs"] = new JsonObject
                {
                    ["images"] = new JsonArray("2", 0),
                    ["filename_prefix"] = "pose-extract"
                }
            }
        };

        var promptId = await ComfyUiWorkflowTransport.SubmitPromptAsync(
            client, baseUrl, graph, Guid.NewGuid().ToString("N"), provider, ReasonPrefix, cancellationToken);

        var history = await ComfyUiWorkflowTransport.WaitForHistoryAsync(
            client, baseUrl, promptId, request.TimeoutSeconds, provider, ReasonPrefix, cancellationToken);

        var personJson = ReadPersonInPixels(history, provider, promptId, request.KeepFace);
        _logger.LogDebug(
            "Extracted a pose from a {Bytes}-byte image via {Node} ({Signature}).",
            request.Image.Length, NodeName, NodeSignature);

        return new PoseKeypointExtractionResult(personJson, NodeName, NodeSignature, WorkflowVersion);
    }

    /// <summary>
    /// Pulls the single person out of the estimator's document and scales it into the stored pixel space. Every
    /// refusal names what was wrong with the document, because "the extraction failed" is not actionable: no
    /// keypoints, no person, several people and a short joint list are four different problems.
    ///
    /// Public because it is the whole of the format contract and it is pure: the tests pin the scaling rule against a
    /// recorded document instead of needing a live host, which is what keeps the fractions-versus-pixels mistake from
    /// coming back.
    /// </summary>
    /// <param name="keepFace">
    /// Whether to keep the face channel. Defaults to false because that is the FORMAT's own default — a document with
    /// no face channel is a body pose — while <see cref="PoseKeypointExtractionRequest"/> requires it, so the one path
    /// that reaches this from production states the kind instead of inheriting it.
    /// </param>
    public static string ReadPersonInPixels(
        JsonObject history, string provider, string promptId, bool keepFace = false)
    {
        var raw = history["outputs"]?.AsObject()
            .Select(output => output.Value?["openpose_json"]?.AsArray())
            .Where(array => array is { Count: > 0 })
            .Select(array => array![0]?.GetValue<string>())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new ImageGenerationException(
                $"ComfyUI returned no pose keypoints for prompt {promptId}. The '{NodeName}' node is what emits them, "
                + "so a server without that node cannot extract a pose.",
                provider,
                reasonCode: $"{ReasonPrefix}_no_keypoints");
        }

        var canvas = UnwrapCanvas(raw);

        var width = CanvasDimension(canvas, "canvas_width");
        var height = CanvasDimension(canvas, "canvas_height");

        var people = canvas["people"] as JsonArray;
        if (people is null || people.Count == 0)
        {
            throw new ImageGenerationException(
                "The pose estimator found no person in the image, so there is no pose to store. Use an image with a "
                + "single, unobstructed figure.",
                provider,
                reasonCode: $"{ReasonPrefix}_no_person");
        }

        if (people.Count != 1 || people[0] is not JsonObject person)
        {
            throw new ImageGenerationException(
                $"The pose estimator found {people.Count} people in the image. A pose preset is one person; use an "
                + "image with a single figure.",
                provider,
                reasonCode: $"{ReasonPrefix}_several_people");
        }

        var scaled = new JsonObject
        {
            ["pose_keypoints_2d"] = Scale(person, "pose_keypoints_2d", BodyValues, width, height, provider)
        };

        // Hands travel with the pose: the pack's own poses carry them and the render reads them for a hand-in-frame
        // pose, so dropping them here would silently downgrade every extracted pose.
        foreach (var (key, values) in new[] { ("hand_left_keypoints_2d", HandValues), ("hand_right_keypoints_2d", HandValues) })
        {
            if (person[key] is JsonArray hand && hand.Count == values)
            {
                scaled[key] = Scale(person, key, values, width, height, provider);
            }
        }

        if (keepFace)
        {
            // Asked for on purpose. A head-only pose IS its face, so an answer with no readable face is a refusal
            // rather than a pose that happens to have no face in it.
            var faceValues = (person["face_keypoints_2d"] as JsonArray)?.Count ?? 0;
            if (faceValues != FaceValues)
            {
                throw new ImageGenerationException(
                    $"A head-only pose was asked for, but the pose estimator returned {faceValues / 3} face keypoints "
                    + $"where {FaceValues / 3} are needed. Use an image where the face is visible and reasonably large.",
                    provider,
                    reasonCode: $"{ReasonPrefix}_no_face");
            }

            scaled["face_keypoints_2d"] = Scale(person, "face_keypoints_2d", FaceValues, width, height, provider);
        }

        return scaled.ToJsonString();
    }

    /// <summary>
    /// The document's person-carrying object. The estimator returns a one-element list holding the canvas and its
    /// people; a payload saved by hand may be the person itself, which carries no canvas and so cannot be scaled.
    /// </summary>
    private static JsonObject UnwrapCanvas(string raw)
    {
        JsonNode? document;
        try
        {
            document = JsonNode.Parse(raw);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new ImageGenerationException(
                $"The pose estimator's keypoint document is not valid JSON ({ex.Message}).",
                "ComfyUI",
                reasonCode: $"{ReasonPrefix}_unreadable_document");
        }

        var candidate = document switch
        {
            JsonArray array when array.Count > 0 => array[0],
            _ => document
        };

        return candidate as JsonObject
            ?? throw new ImageGenerationException(
                "The pose estimator's keypoint document has no person object.",
                "ComfyUI",
                reasonCode: $"{ReasonPrefix}_unreadable_document");
    }

    /// <summary>
    /// The canvas the fractions are relative to. Refused by name rather than assumed: without it there is no way to
    /// put the keypoints into the pixel space the store and the renderer use, and guessing a size would store a pose
    /// whose scale is silently wrong.
    /// </summary>
    private static double CanvasDimension(JsonObject canvas, string name)
    {
        var value = canvas[name]?.GetValue<double>() ?? 0;
        if (value <= 0)
        {
            throw new ImageGenerationException(
                $"The pose estimator's document has no usable '{name}', so the keypoints' scale is unknown and they "
                + "cannot be stored as pixels.",
                "ComfyUI",
                reasonCode: $"{ReasonPrefix}_no_canvas_{name}");
        }

        return value;
    }

    /// <summary>
    /// Turns one flat [x, y, confidence, ...] array from canvas fractions into source-image pixels, leaving the
    /// confidence untouched. The array length is checked first: a short list is a partial figure, and storing it would
    /// be storing a pose the render cannot use.
    /// </summary>
    private static JsonArray Scale(
        JsonObject person, string key, int expectedValues, double width, double height, string provider)
    {
        if (person[key] is not JsonArray values)
        {
            throw new ImageGenerationException(
                $"The pose estimator returned no '{key}'.",
                provider,
                reasonCode: $"{ReasonPrefix}_missing_{key}");
        }

        if (values.Count != expectedValues)
        {
            throw new ImageGenerationException(
                $"The pose estimator returned {values.Count} values for '{key}'; {expectedValues} are needed "
                + $"({expectedValues / 3} joints x 3). The estimator did not resolve a full figure in this image.",
                provider,
                reasonCode: $"{ReasonPrefix}_short_{key}");
        }

        var scaled = new JsonArray();
        for (var index = 0; index < values.Count; index += 3)
        {
            var x = values[index]?.GetValue<double>() ?? 0;
            var y = values[index + 1]?.GetValue<double>() ?? 0;
            var confidence = values[index + 2]?.GetValue<double>() ?? 0;

            scaled.Add(JsonValue.Create(x * width));
            scaled.Add(JsonValue.Create(y * height));
            scaled.Add(JsonValue.Create(confidence));
        }

        return scaled;
    }
}
