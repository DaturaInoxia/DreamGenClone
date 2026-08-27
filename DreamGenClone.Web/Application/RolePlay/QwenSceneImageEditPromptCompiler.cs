using System.Text;
using System.Text.Json;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>Defines the versioned vision-to-edit-instruction contract without model or persistence side effects.</summary>
public sealed class QwenSceneImageEditPromptCompiler : ISceneImageEditPromptCompiler
{
    public const string SchemaVersion = "scene-image-edit-compiler-v1";
    public const string SystemPromptVersion = "qwen-edit-rules-v2";
    public const string ResponseSchemaName = "scene_image_edit_compilation";

    private static readonly HashSet<string> RootFields = new(StringComparer.Ordinal)
    {
        "schemaVersion", "status", "sourceSummary", "targets", "requestedChanges", "preserve",
        "clarificationQuestion", "invalidReason", "compiledPrompt"
    };

    public SceneImageEditCompilerMessages BuildMessages(SceneImageEditCompilerContext context)
    {
        if (string.IsNullOrWhiteSpace(context.RawIntent))
            throw new ArgumentException("A non-empty edit intent is required.", nameof(context));
        if (context.ClarificationHistory.Any(item => string.IsNullOrWhiteSpace(item)))
            throw new ArgumentException("Clarification history cannot contain empty entries.", nameof(context));

        var user = new StringBuilder();
        user.AppendLine("Raw edit intent:");
        user.AppendLine(context.RawIntent.Trim());
        if (context.ClarificationHistory.Count > 0)
        {
            user.AppendLine();
            user.AppendLine("Prior clarification answers:");
            foreach (var answer in context.ClarificationHistory)
                user.AppendLine($"- {answer.Trim()}");
        }

        return new SceneImageEditCompilerMessages(
            SchemaVersion,
            SystemPromptVersion,
            BuildSystemMessage(),
            user.ToString().TrimEnd(),
            ResponseSchemaName,
            CreateResponseSchema());
    }

    public SceneImageEditCompilationResult Parse(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            throw new InvalidOperationException("Scene image edit compiler returned empty output.");

        try
        {
            using var document = JsonDocument.Parse(rawResponse);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Scene image edit compiler response root must be an object.");

            var properties = root.EnumerateObject().ToList();
            if (properties.Count != RootFields.Count || properties.Any(property => !RootFields.Contains(property.Name)))
                throw new InvalidOperationException("Scene image edit compiler response has unknown, missing, or duplicate root fields.");

            var schemaVersion = RequiredString(root, "schemaVersion");
            if (!string.Equals(schemaVersion, SchemaVersion, StringComparison.Ordinal))
                throw new InvalidOperationException($"Scene image edit compiler response uses unsupported schema '{schemaVersion}'.");

            var status = RequiredString(root, "status") switch
            {
                "ready" => SceneImageEditCompilationResultStatus.Ready,
                "clarification_required" => SceneImageEditCompilationResultStatus.ClarificationRequired,
                "invalid" => SceneImageEditCompilationResultStatus.Invalid,
                _ => throw new InvalidOperationException("Scene image edit compiler response has an unknown status.")
            };

            var result = new SceneImageEditCompilationResult
            {
                SchemaVersion = schemaVersion,
                Status = status,
                SourceSummary = RequiredString(root, "sourceSummary"),
                Targets = ParseTargets(Required(root, "targets")),
                RequestedChanges = ParseStringArray(Required(root, "requestedChanges"), "requestedChanges"),
                Preserve = ParseStringArray(Required(root, "preserve"), "preserve"),
                ClarificationQuestion = OptionalString(root, "clarificationQuestion"),
                InvalidReason = OptionalString(root, "invalidReason"),
                CompiledPrompt = OptionalString(root, "compiledPrompt")
            };

            NormalizeTerminalState(result);
            return result;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Scene image edit compiler returned malformed JSON.", ex);
        }
    }

    private static string BuildSystemMessage() => """
        You are a vision-grounded compiler for Qwen Image Edit. Inspect the supplied source image and compile the user's request into one concise edit instruction.

        Observe only visible facts needed to satisfy the request. Identify targets with visible locators such as clothing, position, laterality, or nearby objects. Do not invent names, relationships, hidden anatomy, unseen details, or story facts.

        The user's request is authoritative. If they ask to add, remove, or alter a specific visible thing — clothing, an accessory such as glasses, an object (including moving or repositioning it), pose (looking another way, standing, lowering the head, opening the mouth), framing or zoom, or facial expression — compile that change directly. Never reject a request merely because it changes a category named in the preservation list.

        Preserve only what the request did not ask to change: the location and surroundings, the subject's identity, and any unaffected people. When a request changes framing or moves an object, keep the surrounding location and identity intact while applying the change.

        Return clarification_required only when the target is ambiguous (more than one visible candidate) or a visible detail is uncertain. Return invalid only when the request is genuinely impossible or self-contradictory (for example, two mutually exclusive outcomes), the thing to change is not visible in the source, or the content is clearly harmful or illegal. This editor is used for private, consensual adult fictional scenes; do not refuse an edit merely because it is sexual or adult in nature when the target and change are visible and feasible. Never guess a ready edit.

        Ready instructions must be direct and feasible, describe only the requested change, and state the specific things to keep unchanged (usually the setting and identity). Return only JSON matching the supplied schema. Do not use markdown fences or explanatory text.
        """;

    private static JsonElement CreateResponseSchema()
    {
        using var document = JsonDocument.Parse("""
                        {
                            "type": "object",
                            "additionalProperties": false,
                            "required": ["schemaVersion", "status", "sourceSummary", "targets", "requestedChanges", "preserve", "clarificationQuestion", "invalidReason", "compiledPrompt"],
                            "properties": {
                                "schemaVersion": { "const": "scene-image-edit-compiler-v1" },
                                "status": { "enum": ["ready", "clarification_required", "invalid"] },
                                "sourceSummary": { "type": "string", "minLength": 1 },
                                "targets": {
                                    "type": "array",
                                    "items": {
                                        "type": "object",
                                        "additionalProperties": false,
                                        "required": ["key", "visibleLocator", "region"],
                                        "properties": {
                                            "key": { "type": "string", "minLength": 1 },
                                            "visibleLocator": { "type": "string", "minLength": 1 },
                                            "region": {
                                                "anyOf": [
                                                    { "type": "null" },
                                                    {
                                                        "type": "object",
                                                        "additionalProperties": false,
                                                        "required": ["x", "y", "width", "height"],
                                                        "properties": {
                                                            "x": { "type": "number", "minimum": 0, "maximum": 1 },
                                                            "y": { "type": "number", "minimum": 0, "maximum": 1 },
                                                            "width": { "type": "number", "exclusiveMinimum": 0, "maximum": 1 },
                                                            "height": { "type": "number", "exclusiveMinimum": 0, "maximum": 1 }
                                                        }
                                                    }
                                                ]
                                            }
                                        }
                                    }
                                },
                                "requestedChanges": { "type": "array", "items": { "type": "string", "minLength": 1 } },
                                "preserve": { "type": "array", "items": { "type": "string", "minLength": 1 } },
                                "clarificationQuestion": { "type": ["string", "null"] },
                                "invalidReason": { "type": ["string", "null"] },
                                "compiledPrompt": { "type": ["string", "null"] }
                            }
                        }
            """);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Normalizes the terminal result so the declared status is authoritative, then validates that
    /// the status's essential field is present. Vision models frequently emit an empty string
    /// instead of null for unused terminal fields (for example <c>"compiledPrompt": ""</c>), or
    /// populate a second terminal field; those formatting slips are discarded deterministically
    /// rather than failing the whole compilation. Nothing is fabricated: if the essential field for
    /// the declared status is missing, the parse still fails.
    /// </summary>
    private static void NormalizeTerminalState(SceneImageEditCompilationResult result)
    {
        switch (result.Status)
        {
            case SceneImageEditCompilationResultStatus.Ready:
                if (result.Targets.Count == 0 || result.RequestedChanges.Count == 0 || result.Preserve.Count == 0
                    || string.IsNullOrWhiteSpace(result.CompiledPrompt))
                    throw new InvalidOperationException("A ready compiler result requires targets, changes, preservation, and a compiled prompt only.");
                result.ClarificationQuestion = null;
                result.InvalidReason = null;
                break;
            case SceneImageEditCompilationResultStatus.ClarificationRequired:
                if (string.IsNullOrWhiteSpace(result.ClarificationQuestion))
                    throw new InvalidOperationException("A clarification compiler result requires a clarification question and no executable prompt.");
                result.CompiledPrompt = null;
                result.InvalidReason = null;
                break;
            case SceneImageEditCompilationResultStatus.Invalid:
                if (string.IsNullOrWhiteSpace(result.InvalidReason))
                    throw new InvalidOperationException("An invalid compiler result requires an invalid reason and no executable prompt.");
                result.CompiledPrompt = null;
                result.ClarificationQuestion = null;
                break;
            default:
                throw new InvalidOperationException("Scene image edit compiler response has a non-terminal status.");
        }
    }

    private static List<SceneImageEditTarget> ParseTargets(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Compiler targets must be an array.");

        var targets = new List<SceneImageEditTarget>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in element.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("A compiler target must be an object.");
            var properties = value.EnumerateObject().ToList();
            if (properties.Count != 3 || properties.Any(property => property.Name is not ("key" or "visibleLocator" or "region")))
                throw new InvalidOperationException("A compiler target has unknown, missing, or duplicate fields.");

            var key = RequiredString(value, "key");
            if (!keys.Add(key))
                throw new InvalidOperationException("Compiler target keys must be unique.");
            targets.Add(new SceneImageEditTarget { Key = key, VisibleLocator = RequiredString(value, "visibleLocator"), Region = ParseRegion(Required(value, "region")) });
        }
        return targets;
    }

    private static SceneImageEditTargetRegion? ParseRegion(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return null;
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Compiler target region must be an object or null.");
        var properties = element.EnumerateObject().ToList();
        if (properties.Count != 4 || properties.Any(property => property.Name is not ("x" or "y" or "width" or "height")))
            throw new InvalidOperationException("Compiler target region has unknown, missing, or duplicate fields.");

        var region = new SceneImageEditTargetRegion { X = RequiredNumber(element, "x"), Y = RequiredNumber(element, "y"), Width = RequiredNumber(element, "width"), Height = RequiredNumber(element, "height") };
        if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0 || region.X + region.Width > 1 || region.Y + region.Height > 1)
            throw new InvalidOperationException("Compiler target region must be normalized and contained within the image.");
        return region;
    }

    private static List<string> ParseStringArray(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Compiler {name} must be an array.");
        var values = new List<string>();
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in element.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                throw new InvalidOperationException($"Compiler {name} entries must be non-empty strings.");
            var text = value.GetString()!.Trim();
            if (!distinct.Add(text))
                throw new InvalidOperationException($"Compiler {name} entries must be unique.");
            values.Add(text);
        }
        return values;
    }

    private static JsonElement Required(JsonElement parent, string name) => parent.TryGetProperty(name, out var value)
        ? value : throw new InvalidOperationException($"Compiler response requires '{name}'.");

    private static string RequiredString(JsonElement parent, string name)
    {
        var value = Required(parent, name);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"Compiler response requires non-empty string '{name}'.");
        return value.GetString()!.Trim();
    }

    private static string? OptionalString(JsonElement parent, string name)
    {
        var value = Required(parent, name);
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind == JsonValueKind.String)
            return string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString()!.Trim();
        throw new InvalidOperationException($"Compiler response field '{name}' must be a non-empty string or null.");
    }

    private static double RequiredNumber(JsonElement parent, string name)
    {
        var value = Required(parent, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || double.IsNaN(number) || double.IsInfinity(number))
            throw new InvalidOperationException($"Compiler response requires finite number '{name}'.");
        return number;
    }
}