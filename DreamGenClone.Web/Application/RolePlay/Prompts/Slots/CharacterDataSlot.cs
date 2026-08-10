using System.Text;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 5, Zone B — Actor-aware character data. Full self + partners, comparison-only for non-present.
/// Merged appearance + behavioral text. Trimmable (priority 2).
/// FR-010, FR-011.
/// </summary>
public sealed class CharacterDataSlot : IPromptSlot
{
    private readonly ILogger<CharacterDataSlot> _logger;

    public PromptSlotId Id => PromptSlotId.CharacterData;
    public PromptZone Zone => PromptZone.B;
    public int Order => 5;
    public bool IsTrimEligible => true;

    public CharacterDataSlot(ILogger<CharacterDataSlot> logger)
    {
        _logger = logger;
    }

    public bool ShouldWrite(PromptBuildContext context) => true;

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        var profile = context.ActorProfile;
        var session = context.Session;
        var scenario = context.Scenario;
        var characters = scenario.Characters;
        var variant = context.Variant;

        var sb = new StringBuilder();

        // ── Narrative variant: all characters, lighter format ──
        if (variant == PromptVariant.Narrative || profile.Kind == ActorProfileKind.Narrative)
        {
            sb.AppendLine("Characters in this scene:");
            foreach (var character in characters)
            {
                if (string.IsNullOrWhiteSpace(character.Name)) continue;
                var roleText = string.IsNullOrWhiteSpace(character.Role)
                    ? string.Empty
                    : $" [Role: {character.Role.Trim()}]";
                sb.AppendLine($"  {character.Name}{roleText}");

                // Lighter format: use rich character data if available.
                if (context.CharacterDetails is not null &&
                    context.CharacterDetails.TryGetValue(character.Id, out var detail))
                {
                    if (!string.IsNullOrWhiteSpace(detail.Description))
                        sb.AppendLine($"    {detail.Description.Trim()}");
                    if (!string.IsNullOrWhiteSpace(detail.AppearanceText))
                        sb.AppendLine($"    {detail.AppearanceText}");
                }
            }

            AppendCharacterRoleIntents(sb, characters);
            return Task.FromResult(sb.ToString().TrimEnd());
        }

        // ── Character variant: full self + partners, comparison-only for non-present ──
        var isPresent = new HashSet<string>(profile.PresentCharacterIds, StringComparer.OrdinalIgnoreCase);
        var actorName = profile.ActorName;

        // Persona (Player actor)
        if (profile.Kind == ActorProfileKind.Player)
        {
            // Full self
            if (!string.IsNullOrWhiteSpace(session.PersonaDescription))
            {
                sb.AppendLine($"POV Persona ({session.PersonaName}):");
                sb.AppendLine(session.PersonaDescription.Trim());
                var personaAppearance = PhysicalAttributesFormatter.FormatBlock(
                    session.PersonaPhysicalAttributes, session.PersonaGender);
                if (!string.IsNullOrEmpty(personaAppearance))
                    sb.AppendLine(personaAppearance);

                // Intimate behavioral self-awareness for persona
                if (session.PersonaPhysicalAttributes is not null)
                {
                    var awarenessLevel = ResolvePersonaAwarenessLevel(session);
                    var selfAwareness = IntimateBehavioralTextBuilder.BuildSelfAwarenessText(
                        session.PersonaPhysicalAttributes, session.PersonaGender,
                        awarenessLevel, session.PersonaName);
                    if (!string.IsNullOrEmpty(selfAwareness))
                        sb.AppendLine(selfAwareness);
                }
            }
            else if (session.PersonaName != "You")
            {
                sb.AppendLine($"POV Persona: {session.PersonaName}");
            }
        }
        else
        {
            // NPC actor: render self-description from character details.
            var selfChar = characters.FirstOrDefault(c =>
                string.Equals(c.Name, actorName, StringComparison.OrdinalIgnoreCase));
            if (selfChar is not null && context.CharacterDetails is not null &&
                context.CharacterDetails.TryGetValue(selfChar.Id, out var selfDetail))
            {
                sb.AppendLine($"POV Character: {actorName}");
                if (!string.IsNullOrWhiteSpace(selfDetail.Description))
                    sb.AppendLine($"  {selfDetail.Description.Trim()}");
                if (!string.IsNullOrWhiteSpace(selfDetail.AppearanceText))
                    sb.AppendLine($"  {selfDetail.AppearanceText}");
            }
        }

        // Other characters
        sb.AppendLine("Characters in this scene:");
        foreach (var character in characters)
        {
            if (string.IsNullOrWhiteSpace(character.Name)) continue;

            // Skip persona - already rendered above.
            if (profile.Kind == ActorProfileKind.Player &&
                string.Equals(character.Name, session.PersonaName, StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip self for NPC actors (already rendered as POV)
            if (profile.Kind != ActorProfileKind.Player &&
                string.Equals(character.Name, actorName, StringComparison.OrdinalIgnoreCase))
                continue;

            var isCharPresent = isPresent.Contains(character.Id);
            var roleText = string.IsNullOrWhiteSpace(character.Role)
                ? string.Empty
                : $" [Role: {character.Role.Trim()}]";

            if (!isCharPresent)
            {
                // Comparison-only: one line with endowment/stamina/skill reference.
                sb.AppendLine($"  {character.Name}{roleText} (comparison reference only)");
                if (context.CharacterDetails is not null &&
                    context.CharacterDetails.TryGetValue(character.Id, out var detail))
                {
                    if (!string.IsNullOrWhiteSpace(detail.ComparisonText))
                        sb.AppendLine($"    {detail.ComparisonText}");
                }
            }
            else
            {
                // Full detail for present characters.
                sb.AppendLine($"  {character.Name}{roleText}:");
                if (context.CharacterDetails is not null &&
                    context.CharacterDetails.TryGetValue(character.Id, out var detail))
                {
                    if (!string.IsNullOrWhiteSpace(detail.Description))
                        sb.AppendLine($"    {detail.Description.Trim()}");
                    if (!string.IsNullOrWhiteSpace(detail.AppearanceText))
                        sb.AppendLine($"    {detail.AppearanceText}");
                }
            }
        }

        AppendCharacterRoleIntents(sb, characters);

        _logger.LogDebug(
            "CharacterDataSlot: SessionId={SessionId} Variant={Variant} Kind={Kind} Actor={Actor} CharCount={CharCount}",
            context.Session.Id, variant, profile.Kind, actorName, characters.Count);

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    /// <summary>
    /// Appends the per-role narrative job (role intents) for each character whose
    /// role is recognized by <see cref="SteerRoleIntentCatalog"/>. Sourced from the
    /// single catalog — no fallback text; unknown/unassigned roles are omitted so the
    /// prompt stays state-neutral and non-prescriptive for scripts.
    /// </summary>
    private static void AppendCharacterRoleIntents(StringBuilder sb, IReadOnlyList<ScenarioCharacter> characters)
    {
        var roleIntents = new List<(string Name, string Role, string Intent)>();
        foreach (var character in characters)
        {
            if (string.IsNullOrWhiteSpace(character.Name) || string.IsNullOrWhiteSpace(character.Role))
                continue;

            var role = character.Role.Trim();
            var context = SteerRoleIntentCatalog.GetRoleContext(role);

            // Skip the generic fallback for unknown roles — only recognized roles carry a role intent.
            if (string.Equals(context, "No specific narrative role — steer based on recent scene context and character disposition.", StringComparison.OrdinalIgnoreCase))
                continue;

            roleIntents.Add((character.Name.Trim(), role, context));
        }

        if (roleIntents.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("Character Role Intents:");
        foreach (var (name, role, context) in roleIntents)
        {
            sb.AppendLine($"  {name} ({role}): {context}");
        }
    }

    public string Trim(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        // Trim non-present character data first, then from end.
        return text[..Math.Max(1, maxChars)];
    }

    private static int? ResolvePersonaAwarenessLevel(RolePlaySession session)
    {
        // Default to moderate (level 2) for MVP; configurable in later phases.
        return 2;
    }
}
