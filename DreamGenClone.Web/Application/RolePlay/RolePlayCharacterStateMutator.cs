using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public static class RolePlayCharacterStateMutator
{
    public static void ClearLocationState(AdaptiveScenarioState state)
    {
        state.CurrentSceneLocation = null;
        state.CharacterLocations = [];
        state.CharacterLocationPerceptions = [];
    }

    public static void EnsureCharacterLocationRows(AdaptiveScenarioState state)
    {
        foreach (var snapshot in state.CharacterSnapshots)
        {
            if (string.IsNullOrWhiteSpace(snapshot.CharacterId)) continue;

            if (!state.CharacterLocations.Any(x => string.Equals(x.CharacterId, snapshot.CharacterId, StringComparison.OrdinalIgnoreCase)))
            {
                state.CharacterLocations.Add(new CharacterLocationState
                {
                    CharacterId = snapshot.CharacterId,
                    TrueLocation = null,
                    UpdatedUtc = DateTime.UtcNow
                });
            }
        }
    }

    public static void UpsertTrueLocation(AdaptiveScenarioState state, string characterId, string? trueLocation, bool sourceIsHidden)
    {
        var row = state.CharacterLocations.FirstOrDefault(x => string.Equals(x.CharacterId, characterId, StringComparison.OrdinalIgnoreCase));
        if (row is null) { row = new CharacterLocationState { CharacterId = characterId }; state.CharacterLocations.Add(row); }
        row.TrueLocation = trueLocation;
        row.IsHidden = sourceIsHidden;
        row.UpdatedUtc = DateTime.UtcNow;
    }

    public static void UpdatePerceivedLocationsFromTruth(AdaptiveScenarioState state)
    {
        var truthByActor = state.CharacterLocations.Where(x => !string.IsNullOrWhiteSpace(x.CharacterId)).ToDictionary(x => x.CharacterId, x => x, StringComparer.OrdinalIgnoreCase);
        if (truthByActor.Count == 0) return;

        foreach (var observer in truthByActor.Values)
        {
            foreach (var target in truthByActor.Values)
            {
                var row = state.CharacterLocationPerceptions.FirstOrDefault(x => string.Equals(x.ObserverCharacterId, observer.CharacterId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.TargetCharacterId, target.CharacterId, StringComparison.OrdinalIgnoreCase));
                if (row is null) { row = new CharacterLocationPerceptionState { ObserverCharacterId = observer.CharacterId, TargetCharacterId = target.CharacterId }; state.CharacterLocationPerceptions.Add(row); }

                if (string.Equals(observer.CharacterId, target.CharacterId, StringComparison.OrdinalIgnoreCase))
                {
                    row.PerceivedLocation = observer.TrueLocation; row.Confidence = 100; row.HasLineOfSight = true; row.IsInProximity = true; row.KnowledgeSource = "self"; row.UpdatedUtc = DateTime.UtcNow;
                    continue;
                }
                var sameLocation = !string.IsNullOrWhiteSpace(observer.TrueLocation) && string.Equals(observer.TrueLocation, target.TrueLocation, StringComparison.OrdinalIgnoreCase);
                if (sameLocation && !target.IsHidden)
                {
                    row.PerceivedLocation = target.TrueLocation; row.Confidence = 100; row.HasLineOfSight = true; row.IsInProximity = true; row.KnowledgeSource = "line-of-sight"; row.UpdatedUtc = DateTime.UtcNow;
                    continue;
                }
                row.HasLineOfSight = false; row.IsInProximity = false;
                if (string.IsNullOrWhiteSpace(row.PerceivedLocation))
                {
                    if (string.IsNullOrWhiteSpace(target.TrueLocation)) { row.Confidence = 0; row.KnowledgeSource = "unknown"; row.UpdatedUtc = DateTime.UtcNow; continue; }
                    row.PerceivedLocation = target.TrueLocation; row.Confidence = 35; row.KnowledgeSource = "assumed";
                }
                else { row.Confidence = Math.Clamp(row.Confidence - 15, 20, 85); row.KnowledgeSource = "last-known"; }
                row.UpdatedUtc = DateTime.UtcNow;
            }
        }
    }
}