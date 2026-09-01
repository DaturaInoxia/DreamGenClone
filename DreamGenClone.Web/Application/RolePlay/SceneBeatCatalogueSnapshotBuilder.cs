using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed record SceneBeatCatalogueInputSnapshot(
    int SchemaVersion,
    string SessionId,
    string TurnId,
    int TurnIndex,
    string TurnKind,
    DateTime StartedUtc,
    DateTime CompletedUtc,
    string TurnMembershipSha256,
    IReadOnlyList<SceneBeatCatalogueEvidenceSnapshot> Evidence,
    IReadOnlyList<SceneBeatCatalogueProfileSnapshot> Profiles);

public sealed record SceneBeatCatalogueEvidenceSnapshot(
    string Key,
    int SourceOrder,
    string InteractionId,
    string ActorName,
    string InteractionType,
    string Content,
    DateTime CreatedAt,
    string SourceSha256);

public sealed record SceneBeatCatalogueProfileSnapshot(
    string Key,
    string? CharacterId,
    string Name,
    string Role,
    string Gender,
    string Description,
    string Appearance,
    string Clothing,
    bool IsPersona,
    string SourceSha256);

public sealed class SceneBeatCatalogueSnapshotBuilder
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public SceneBeatCatalogueInputSnapshot Build(
        FullTurnContext fullTurn,
        RolePlaySession session,
        IReadOnlyList<Character>? characters)
    {
        ArgumentNullException.ThrowIfNull(fullTurn);
        ArgumentNullException.ThrowIfNull(session);

        var turn = fullTurn.Turn
            ?? throw new InvalidOperationException("Beat Catalogue snapshot creation requires a persisted RolePlayV2Turn.");
        if (turn.Status != DreamGenClone.Domain.RolePlay.RolePlayTurnStatus.Completed || turn.CompletedUtc is null)
            throw new InvalidOperationException($"Beat Catalogue snapshot creation requires completed turn '{turn.TurnId}'.");
        if (!string.Equals(turn.SessionId, session.Id, StringComparison.Ordinal))
            throw new InvalidOperationException($"Turn '{turn.TurnId}' does not belong to session '{session.Id}'.");
        if (turn.OutputInteractionIds.Count == 0)
            throw new InvalidOperationException($"Turn '{turn.TurnId}' has no authoritative output interactions.");

        var membership = turn.OutputInteractionIds
            .Prepend(turn.InputInteractionId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToList();
        if (membership.Distinct(StringComparer.OrdinalIgnoreCase).Count() != membership.Count)
            throw new InvalidOperationException($"Turn '{turn.TurnId}' contains duplicate interaction membership.");

        var interactionGroups = fullTurn.Interactions
            .GroupBy(interaction => interaction.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (interactionGroups.Any(group => group.Count() != 1))
            throw new InvalidOperationException($"Turn '{turn.TurnId}' contains duplicate loaded interactions.");

        var membershipSet = membership.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var interactionById = interactionGroups.ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);
        var missingIds = membership.Where(id => !interactionById.ContainsKey(id)).ToList();
        if (missingIds.Count > 0)
            throw new InvalidOperationException($"Turn '{turn.TurnId}' is missing authoritative interactions: {string.Join(", ", missingIds)}.");
        if (interactionById.Keys.Any(id => !membershipSet.Contains(id)))
            throw new InvalidOperationException($"Turn '{turn.TurnId}' includes interactions outside its authoritative membership.");

        var orderedInteractions = membership
            .Select(id => interactionById[id])
            .OrderBy(interaction => interaction.CreatedAt)
            .ThenBy(interaction => interaction.Id, StringComparer.Ordinal)
            .ToList();
        var narrativeInteractions = orderedInteractions
            .Where(interaction => string.Equals(interaction.ActorName, "Narrative", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (narrativeInteractions.Count != 1)
            throw new InvalidOperationException($"Turn '{turn.TurnId}' must contain exactly one authoritative Narrative interaction.");

        var narrative = narrativeInteractions[0];
        var evidence = new List<SceneBeatCatalogueEvidenceSnapshot>
        {
            CreateEvidence("n0", orderedInteractions.IndexOf(narrative), narrative)
        };
        evidence.AddRange(orderedInteractions
            .Where(interaction => !ReferenceEquals(interaction, narrative))
            .Select((interaction, index) => CreateEvidence($"c{index + 1}", orderedInteractions.IndexOf(interaction), interaction)));

        return new SceneBeatCatalogueInputSnapshot(
            CurrentSchemaVersion,
            session.Id,
            turn.TurnId,
            turn.TurnIndex,
            turn.TurnKind,
            turn.StartedUtc,
            turn.CompletedUtc.Value,
            Hash(membership),
            evidence,
            CreateProfiles(session, characters));
    }

    public string Serialize(SceneBeatCatalogueInputSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    public IReadOnlyList<string> ResolveEvidenceInteractionIds(
        SceneBeatCatalogueInputSnapshot snapshot,
        IReadOnlyList<string> evidenceKeys)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return ResolveKeys(snapshot.Evidence, evidenceKeys, item => item.Key, item => item.InteractionId, "evidence");
    }

    public IReadOnlyList<string?> ResolveProfileCharacterIds(
        SceneBeatCatalogueInputSnapshot snapshot,
        IReadOnlyList<string> profileKeys)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return ResolveKeys(snapshot.Profiles, profileKeys, item => item.Key, item => item.CharacterId, "profile");
    }

    private static IReadOnlyList<SceneBeatCatalogueProfileSnapshot> CreateProfiles(
        RolePlaySession session,
        IReadOnlyList<Character>? characters)
    {
        var sources = (characters ?? [])
            .Where(character => !character.IsPersona)
            .Select(character => new ProfileSource(
                character.Id,
                RequiredName(character.Name, "Scenario character"),
                character.Role,
                character.Gender,
                character.Description ?? string.Empty,
                PhysicalAttributesFormatter.FormatVisualBlock(character.PhysicalAttributes),
                PhysicalAttributesFormatter.FormatVisualClothing(character.PhysicalAttributes),
                false))
            .Append(new ProfileSource(
                session.PersonaCharacterId,
                RequiredName(session.PersonaName, "Session persona"),
                session.PersonaRole,
                session.PersonaGender,
                session.PersonaDescription,
                PhysicalAttributesFormatter.FormatVisualBlock(session.PersonaPhysicalAttributes),
                PhysicalAttributesFormatter.FormatVisualClothing(session.PersonaPhysicalAttributes),
                true))
            .OrderBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.CharacterId, StringComparer.Ordinal)
            .ToList();

        if (sources.Select(source => source.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Count)
            throw new InvalidOperationException("Beat Catalogue snapshot character names must be unique.");

        return sources.Select((source, index) => new SceneBeatCatalogueProfileSnapshot(
            $"p{index}",
            source.CharacterId,
            source.Name,
            source.Role,
            source.Gender,
            source.Description,
            source.Appearance,
            source.Clothing,
            source.IsPersona,
            Hash([
                source.CharacterId ?? string.Empty,
                source.Name,
                source.Role,
                source.Gender,
                source.Description,
                source.Appearance,
                source.Clothing,
                source.IsPersona.ToString()
            ]))).ToList();
    }

    private static SceneBeatCatalogueEvidenceSnapshot CreateEvidence(
        string key,
        int sourceOrder,
        RolePlayInteraction interaction)
        => new(
            key,
            sourceOrder,
            interaction.Id,
            interaction.ActorName,
            interaction.InteractionType.ToString(),
            interaction.Content,
            interaction.CreatedAt,
            Hash([
                interaction.Id,
                interaction.ActorName,
                interaction.InteractionType.ToString(),
                interaction.Content,
                interaction.CreatedAt.ToUniversalTime().ToString("O")
            ]));

    private static IReadOnlyList<TResult> ResolveKeys<TSource, TResult>(
        IReadOnlyList<TSource> sources,
        IReadOnlyList<string> keys,
        Func<TSource, string> keySelector,
        Func<TSource, TResult> valueSelector,
        string keyKind)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
            throw new InvalidOperationException($"At least one {keyKind} key is required.");
        if (keys.Any(string.IsNullOrWhiteSpace) || keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != keys.Count)
            throw new InvalidOperationException($"{keyKind} keys must be non-empty and unique.");

        var valuesByKey = sources.ToDictionary(keySelector, valueSelector, StringComparer.OrdinalIgnoreCase);
        var unknownKeys = keys.Where(key => !valuesByKey.ContainsKey(key)).ToList();
        if (unknownKeys.Count > 0)
            throw new InvalidOperationException($"Unknown {keyKind} keys: {string.Join(", ", unknownKeys)}.");
        return keys.Select(key => valuesByKey[key]).ToList();
    }

    private static string RequiredName(string? value, string source)
        => !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new InvalidOperationException($"{source} requires a name for Beat Catalogue snapshot creation.");

    private static string Hash(IEnumerable<string> values)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001F', values))));

    private sealed record ProfileSource(
        string? CharacterId,
        string Name,
        string Role,
        string Gender,
        string Description,
        string Appearance,
        string Clothing,
        bool IsPersona);
}