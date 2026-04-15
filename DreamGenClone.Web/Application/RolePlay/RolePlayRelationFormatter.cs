using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.RolePlay;

public static class RolePlayRelationFormatter
{
    public static string? DescribePersonaRelation(RolePlaySession session, IReadOnlyList<Character>? characters)
    {
        return DescribeTarget(session.PersonaRelationTargetId, characters, session.PersonaName, session.PersonaRole, session.PersonaTemplateId);
    }

    public static string? DescribeCharacterRelation(Character character, RolePlaySession? session, IReadOnlyList<Character>? characters)
    {
        var personaName = session is null || string.IsNullOrWhiteSpace(session.PersonaName)
            ? CharacterRelationCatalog.PersonaDisplayLabel
            : session.PersonaName;
        var personaRole = session?.PersonaRole ?? CharacterRoleCatalog.Unknown;
        var personaTemplateId = session?.PersonaTemplateId;
        return DescribeTarget(character.RelationTargetId, characters, personaName, personaRole, personaTemplateId);
    }

    public static string? DescribeTarget(string? relationTargetId, IReadOnlyList<Character>? characters, string? personaName, string? personaRole, string? personaTemplateId = null)
    {
        var normalizedTargetId = CharacterRelationCatalog.NormalizeTargetId(relationTargetId);
        if (string.IsNullOrWhiteSpace(normalizedTargetId))
        {
            return null;
        }

        if (CharacterRelationCatalog.IsPersonaTarget(normalizedTargetId))
        {
            var resolvedPersonaName = string.IsNullOrWhiteSpace(personaName)
                ? CharacterRelationCatalog.PersonaDisplayLabel
                : personaName.Trim();
            var normalizedPersonaRole = CharacterRoleCatalog.Normalize(personaRole);
            return string.Equals(normalizedPersonaRole, CharacterRoleCatalog.Unknown, StringComparison.OrdinalIgnoreCase)
                ? $"{resolvedPersonaName} (Persona)"
                : $"{resolvedPersonaName} (Persona, Role: {normalizedPersonaRole})";
        }

        var targetPersonaTemplateId = CharacterRelationCatalog.TryGetPersonaTemplateId(normalizedTargetId);
        if (!string.IsNullOrWhiteSpace(targetPersonaTemplateId))
        {
            if (!string.Equals(targetPersonaTemplateId, personaTemplateId, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var resolvedPersonaName = string.IsNullOrWhiteSpace(personaName)
                ? CharacterRelationCatalog.PersonaDisplayLabel
                : personaName.Trim();
            var normalizedPersonaRole = CharacterRoleCatalog.Normalize(personaRole);
            return string.Equals(normalizedPersonaRole, CharacterRoleCatalog.Unknown, StringComparison.OrdinalIgnoreCase)
                ? $"{resolvedPersonaName} (Persona)"
                : $"{resolvedPersonaName} (Persona, Role: {normalizedPersonaRole})";
        }

        var target = characters?.FirstOrDefault(x => string.Equals(x.Id, normalizedTargetId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return $"Unknown target ({normalizedTargetId})";
        }

        var name = string.IsNullOrWhiteSpace(target.Name) ? "Unnamed" : target.Name.Trim();
        var normalizedRole = CharacterRoleCatalog.Normalize(target.Role);
        return string.Equals(normalizedRole, CharacterRoleCatalog.Unknown, StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{name} (Role: {normalizedRole})";
    }
}