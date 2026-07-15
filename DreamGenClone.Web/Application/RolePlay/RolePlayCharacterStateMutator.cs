namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Shared static helper for per-character location state mutation.
/// Extracted from <see cref="RolePlayEngineService"/> so the
/// <see cref="LocationDetectionJobHandler"/> can call these helpers without
/// reaching into private engine methods.
/// </summary>
public static class RolePlayCharacterStateMutator
{
    public static void ClearLocationState(DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state)
    {
        state.CurrentSceneLocation = null;
        state.CharacterLocations = [];
        state.CharacterLocationPerceptions = [];
    }

    public static void EnsureCharacterLocationRows(DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state)
    {
        foreach (var snapshot in state.CharacterSnapshots)
        {
            if (string.IsNullOrWhiteSpace(snapshot.CharacterId))
                continue;

            if (!state.CharacterLocations.Any(x =>
                    string.Equals(x.CharacterId, snapshot.CharacterId, StringComparison.OrdinalIgnoreCase)))
            {
                state.CharacterLocations.Add(new DreamGenClone.Domain.RolePlay.CharacterLocationState
                {
                    CharacterId = snapshot.CharacterId,
                    TrueLocation = null,
                    UpdatedUtc = DateTime.UtcNow
                });
            }
        }
    }

    public static void UpsertTrueLocation(
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        string characterId,
        string? trueLocation,
        bool sourceIsHidden)
    {
        var row = state.CharacterLocations.FirstOrDefault(x =>
            string.Equals(x.CharacterId, characterId, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            row = new DreamGenClone.Domain.RolePlay.CharacterLocationState { CharacterId = characterId };
            state.CharacterLocations.Add(row);
        }

        row.TrueLocation = trueLocation;
        row.IsHidden = sourceIsHidden;
        row.UpdatedUtc = DateTime.UtcNow;
    }

    public static void UpdatePerceivedLocationsFromTruth(DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state)
    {
        var truthByActor = state.CharacterLocations
            .Where(x => !string.IsNullOrWhiteSpace(x.CharacterId))
            .ToDictionary(x => x.CharacterId, x => x, StringComparer.OrdinalIgnoreCase);
        if (truthByActor.Count == 0)
            return;

        foreach (var observer in truthByActor.Values)
        {
            foreach (var target in truthByActor.Values)
            {
                var row = state.CharacterLocationPerceptions.FirstOrDefault(x =>
                    string.Equals(x.ObserverCharacterId, observer.CharacterId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.TargetCharacterId, target.CharacterId, StringComparison.OrdinalIgnoreCase));
                if (row is null)
                {
                    row = new DreamGenClone.Domain.RolePlay.CharacterLocationPerceptionState
                    {
                        ObserverCharacterId = observer.CharacterId,
                        TargetCharacterId = target.CharacterId
                    };
                    state.CharacterLocationPerceptions.Add(row);
                }

                if (string.Equals(observer.CharacterId, target.CharacterId, StringComparison.OrdinalIgnoreCase))
                {
                    row.PerceivedLocation = observer.TrueLocation;
                    row.Confidence = 100;
                    row.HasLineOfSight = true;
                    row.IsInProximity = true;
                    row.KnowledgeSource = "self";
                    row.UpdatedUtc = DateTime.UtcNow;
                    continue;
                }

                var sameLocation = !string.IsNullOrWhiteSpace(observer.TrueLocation)
                    && string.Equals(observer.TrueLocation, target.TrueLocation, StringComparison.OrdinalIgnoreCase);
                if (sameLocation && !target.IsHidden)
                {
                    row.PerceivedLocation = target.TrueLocation;
                    row.Confidence = 100;
                    row.HasLineOfSight = true;
                    row.IsInProximity = true;
                    row.KnowledgeSource = "direct";
                    row.UpdatedUtc = DateTime.UtcNow;
                    continue;
                }

                row.PerceivedLocation = null;
                row.Confidence = 0;
                row.HasLineOfSight = false;
                row.IsInProximity = false;
                row.KnowledgeSource = "no_intel";
                row.UpdatedUtc = DateTime.UtcNow;
            }
        }
    }
}
