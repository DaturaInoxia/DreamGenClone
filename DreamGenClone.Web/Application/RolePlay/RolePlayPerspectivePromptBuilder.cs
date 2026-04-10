using System.Text;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

internal static class RolePlayPerspectivePromptBuilder
{
    public static void AppendInteractionInstruction(
        StringBuilder sb,
        CharacterPerspectiveMode mode,
        string actorName,
        string personaName,
        string styleHint,
        string lengthInstruction)
    {
        switch (mode)
        {
            case CharacterPerspectiveMode.FirstPersonInternalMonologue:
                sb.AppendLine($"Write the next interaction as {actorName} in FIRST PERSON. Use \"I\" throughout. " +
                    $"Include {actorName}'s dialogue, actions, physical sensations, and internal thoughts. " +
                    "Refer to all other characters by name in third person. " +
                    $"Match the scene's style ({styleHint}). {lengthInstruction}");
                break;

            case CharacterPerspectiveMode.FirstPersonExternalOnly:
                sb.AppendLine($"Write the next interaction as {actorName} in FIRST PERSON. Use \"I\" throughout. " +
                    $"Include {actorName}'s dialogue, actions, sensory experience, and externally expressed reactions. " +
                    $"Do NOT directly state {actorName}'s internal thoughts or hidden feelings. " +
                    "Refer to all other characters by name in third person. " +
                    $"Match the scene's style ({styleHint}). {lengthInstruction}");
                break;

            case CharacterPerspectiveMode.ThirdPersonLimited:
                sb.AppendLine($"Write the next interaction for {actorName} in THIRD PERSON LIMITED. " +
                    $"Use \"{actorName}\" and \"he/she/they\" — NEVER use \"I\" for {actorName}. " +
                    $"Include {actorName}'s dialogue, actions, physical sensations, and internal thoughts, but keep the passage anchored to {actorName}'s private perspective only. " +
                    $"Do NOT write from {personaName}'s perspective unless {personaName} is {actorName}. " +
                    $"Match the scene's style ({styleHint}). {lengthInstruction}");
                break;

            default:
                sb.AppendLine($"Write the next interaction for {actorName} in THIRD PERSON. " +
                    $"Use \"{actorName}\" and \"he/she/they\" — NEVER use \"I\" or first person for {actorName}. " +
                    $"Include {actorName}'s dialogue (in quotes), physical actions, body language, and observable behavior. " +
                    $"Do NOT write {actorName}'s internal thoughts or feelings — only what can be seen and heard externally. " +
                    $"Do NOT write from {personaName}'s perspective or include {personaName}'s thoughts. " +
                    $"Match the scene's style ({styleHint}). {lengthInstruction}");
                break;
        }
    }
}