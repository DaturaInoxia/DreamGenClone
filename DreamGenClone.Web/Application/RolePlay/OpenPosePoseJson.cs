using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// One OpenPose keypoint. <see cref="X"/> and <see cref="Y"/> are in source-image pixels with y growing
/// down, and <see cref="Confidence"/> is the detector score. A keypoint at or below the visibility floor is
/// absent, not a point at the origin — the distinction is what the pair predicate depends on.
/// </summary>
public readonly record struct PoseKeypoint(double X, double Y, double Confidence);

/// <summary>
/// One person's OpenPose keypoints: the 18-joint COCO body plus the two optional 21-joint hands. Every
/// pose in this app is single-person; a scene that needs two people composes two entries at render time
/// rather than smuggling a crowd through one preset.
/// </summary>
public sealed class PosePerson
{
    /// <summary>COCO-18: nose, neck, shoulders, elbows, wrists, hips, knees, ankles, eyes, ears.</summary>
    public const int BodyJointCount = 18;

    /// <summary>OpenPose hand model: wrist plus four joints on each of five fingers.</summary>
    public const int HandJointCount = 21;

    public required IReadOnlyList<PoseKeypoint> Body { get; init; }

    public IReadOnlyList<PoseKeypoint> LeftHand { get; init; } = [];

    public IReadOnlyList<PoseKeypoint> RightHand { get; init; } = [];
}

/// <summary>
/// Reads and writes the OpenPose keypoint JSON. Two document shapes are accepted because both are genuinely
/// OpenPose: the pack's frame document (<c>{ "people": [ … ] }</c>) and a bare person object, which is what a
/// stored preset holds. Nothing is guessed — a wrong array length or a missing person fails with the count.
/// </summary>
public static class OpenPosePoseJson
{
    /// <summary>
    /// A keypoint counts as visible only above this score. Shared rather than repeated, because a lenient
    /// re-derivation of this rule once produced a false all-clear on the pose pack (it counted a pose as
    /// having a head when only one of its face keypoints cleared the floor).
    /// </summary>
    public const double VisibilityFloor = 0.1;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = false };

    /// <summary>Index of the nose, the first of the four joints the head-keypoint rule requires.</summary>
    public const int NoseIndex = 0;

    /// <summary>Index of the neck.</summary>
    public const int NeckIndex = 1;

    /// <summary>Index of the right shoulder (OpenPose's own right, i.e. image-left).</summary>
    public const int RightShoulderIndex = 2;

    /// <summary>Index of the left shoulder.</summary>
    public const int LeftShoulderIndex = 5;

    public static PosePerson Parse(string json, string what)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException($"{what}: the keypoints JSON is empty.");

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{what}: the keypoints JSON is not valid JSON ({ex.Message}).", ex);
        }

        if (root is not JsonObject obj)
            throw new InvalidOperationException($"{what}: the keypoints JSON must be an object.");

        var person = obj["people"] is JsonArray people
            ? people.Count == 1
                ? people[0] as JsonObject
                    ?? throw new InvalidOperationException($"{what}: 'people[0]' must be an object.")
                : throw new InvalidOperationException(
                    $"{what}: expected exactly one person but found {people.Count}. A pose preset is one person; "
                    + "compose two people at render time instead of storing a pair.")
            : obj;

        var body = ReadKeypoints(person, "pose_keypoints_2d", PosePerson.BodyJointCount, what);
        var left = ReadOptionalKeypoints(person, "hand_left_keypoints_2d", what);
        var right = ReadOptionalKeypoints(person, "hand_right_keypoints_2d", what);

        return new PosePerson { Body = body, LeftHand = left, RightHand = right };
    }

    /// <summary>
    /// Writes the canonical stored form: a bare person object with the three arrays. Round-trips stably so a
    /// saved preset reloads identically.
    /// </summary>
    public static string Serialize(PosePerson person)
    {
        ArgumentNullException.ThrowIfNull(person);
        if (person.Body.Count != PosePerson.BodyJointCount)
        {
            throw new InvalidOperationException(
                $"A pose must have {PosePerson.BodyJointCount} body keypoints but has {person.Body.Count}.");
        }

        var payload = new Dictionary<string, object?>
        {
            ["pose_keypoints_2d"] = Flatten(person.Body),
            ["hand_left_keypoints_2d"] = Flatten(person.LeftHand),
            ["hand_right_keypoints_2d"] = Flatten(person.RightHand)
        };

        return JsonSerializer.Serialize(payload, WriteOptions);
    }

    /// <summary>
    /// Enforces the head-keypoint rule: the nose, the neck and both shoulders must all clear the floor.
    /// A skeleton with no head leaves the head unconstrained, so the model free-poses it and any identity
    /// reference then drags that free head wherever it likes — the failure is silent and looks like a
    /// perfectly good render. Rejecting names the joints that are missing rather than saying "invalid".
    /// </summary>
    public static void RequireHeadKeypoints(PosePerson person, string what)
    {
        ArgumentNullException.ThrowIfNull(person);
        var required = new (int Index, string Name)[]
        {
            (NoseIndex, "nose"),
            (NeckIndex, "neck"),
            (RightShoulderIndex, "right shoulder"),
            (LeftShoulderIndex, "left shoulder")
        };

        var missing = required
            .Where(r => person.Body[r.Index].Confidence <= VisibilityFloor)
            .Select(r => r.Name)
            .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"{what}: the skeleton is missing {string.Join(", ", missing)} above the visibility floor "
                + $"({VisibilityFloor.ToString(CultureInfo.InvariantCulture)}). A skeleton without a head leaves the head "
                + "unconstrained, so the render would free-pose it. Extract a pose from an image where the head is visible.");
        }
    }

    private static List<PoseKeypoint> ReadKeypoints(JsonObject person, string name, int expected, string what)
    {
        var values = ReadArray(person, name, what, required: true)!;
        if (values.Count != expected * 3)
        {
            throw new InvalidOperationException(
                $"{what}: '{name}' must hold {expected} keypoints ({expected * 3} values) but holds "
                + $"{values.Count} values ({values.Count / 3} keypoints and {values.Count % 3} left over).");
        }

        var result = new List<PoseKeypoint>(expected);
        for (var i = 0; i < expected; i++)
        {
            result.Add(new PoseKeypoint(values[i * 3], values[i * 3 + 1], values[i * 3 + 2]));
        }

        return result;
    }

    private static List<PoseKeypoint> ReadOptionalKeypoints(JsonObject person, string name, string what)
    {
        var values = ReadArray(person, name, what, required: false);
        if (values is null || values.Count == 0) return [];

        if (values.Count != PosePerson.HandJointCount * 3)
        {
            throw new InvalidOperationException(
                $"{what}: '{name}' must hold {PosePerson.HandJointCount} keypoints or be absent, but holds "
                + $"{values.Count} values.");
        }

        var result = new List<PoseKeypoint>(PosePerson.HandJointCount);
        for (var i = 0; i < PosePerson.HandJointCount; i++)
        {
            result.Add(new PoseKeypoint(values[i * 3], values[i * 3 + 1], values[i * 3 + 2]));
        }

        return result;
    }

    private static List<double>? ReadArray(JsonObject person, string name, string what, bool required)
    {
        if (person[name] is not JsonArray array)
        {
            if (required)
                throw new InvalidOperationException($"{what}: '{name}' is missing from the keypoints JSON.");

            return null;
        }

        var values = new List<double>(array.Count);
        foreach (var node in array)
        {
            if (node is null)
                throw new InvalidOperationException($"{what}: '{name}' contains a null value.");

            values.Add(node.GetValue<double>());
        }

        return values;
    }

    private static double[] Flatten(IReadOnlyList<PoseKeypoint> keypoints)
    {
        var flattened = new double[keypoints.Count * 3];
        for (var i = 0; i < keypoints.Count; i++)
        {
            flattened[i * 3] = keypoints[i].X;
            flattened[i * 3 + 1] = keypoints[i].Y;
            flattened[i * 3 + 2] = keypoints[i].Confidence;
        }

        return flattened;
    }
}
