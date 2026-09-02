using System.Text.Json;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>Builds the lightweight "what the model sees" vision description prompt and schema.</summary>
public static class SceneImageDescriptionPromptBuilder
{
    public const string SchemaVersion = "scene-image-description-v1";
    public const string ResponseSchemaName = "scene_image_description";

    public static string BuildSystemMessage() => """
        You are a vision assistant for a scene-image editor. Inspect the supplied source image and describe concisely what is visibly in it.

        Report only visible facts: the main subject(s), clothing, pose, facial expression, objects (including anything near or in the mouth), and the setting/location. Do not invent names, relationships, hidden anatomy, unseen details, or story facts. If something is uncertain, say it is uncertain. This editor is used for private, consensual adult fictional scenes; do not refuse to describe a visible scene for that reason.

        Keep the description to 2-4 sentences. Return only JSON matching the supplied schema. Do not use markdown fences or explanatory text.
        """;

    public static string BuildUserMessage() => "Describe what is visibly in this image.";

    public static JsonElement CreateResponseSchema()
    {
        using var document = JsonDocument.Parse("""
                        {
                            "type": "object",
                            "additionalProperties": false,
                            "required": ["description"],
                            "properties": {
                                "description": { "type": "string", "minLength": 1 }
                            }
                        }
            """);
        return document.RootElement.Clone();
    }
}
