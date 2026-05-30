namespace DreamGenClone.Domain.RolePlay;

public sealed class AdaptiveScenarioState
{
    public string SessionId { get; set; } = string.Empty;
    public string? ActiveScenarioId { get; set; }
    public string? ActiveVariantId { get; set; }
    public NarrativePhase CurrentPhase { get; set; } = NarrativePhase.BuildUp;
    public int InteractionCountInPhase { get; set; }
    public int ConsecutiveLeadCount { get; set; }
    public DateTime LastEvaluationUtc { get; set; } = DateTime.UtcNow;
    public int CycleIndex { get; set; }
    public string ActiveFormulaVersion { get; set; } = string.Empty;
    public string? SelectedWillingnessProfileId { get; set; }
    public string? SelectedNarrativeGateProfileId { get; set; }

    /// <summary>
    /// Maps characterId → CharacterProfile.Id for encounter behavioral profile bindings.
    /// One entry per character that has an encounter profile bound.
    /// Empty dictionary means no encounter profiles are bound for any character.
    /// </summary>
    public Dictionary<string, string> CharacterEncounterProfileIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public NarrativePhase? PhaseOverrideFloor { get; set; }
    public string? PhaseOverrideScenarioId { get; set; }
    public int? PhaseOverrideCycleIndex { get; set; }
    public string? PhaseOverrideSource { get; set; }
    public DateTime? PhaseOverrideAppliedUtc { get; set; }
    public string? CurrentSceneLocation { get; set; }
    public List<CharacterLocationState> CharacterLocations { get; set; } = [];
    public List<CharacterLocationPerceptionState> CharacterLocationPerceptions { get; set; } = [];
    public List<CharacterStatProfileV2> CharacterSnapshots { get; set; } = [];

    // ---- Runtime CharacterStats dictionary (not persisted) --------------------------------
    // Lazy-initialised from CharacterSnapshots on first access. Shares object references with
    // CharacterSnapshots for existing entries. Call SyncCharacterSnapshots() before persistence
    // whenever entries are added or removed.
    [System.Text.Json.Serialization.JsonIgnore]
    private Dictionary<string, CharacterStatProfileV2>? _characterStats;

    /// <summary>
    /// Runtime in-memory dictionary of character stat profiles, keyed by CharacterId.
    /// Lazily built from <see cref="CharacterSnapshots"/>. The dictionary and the list share
    /// object references, so in-place mutations are visible from both sides.
    /// Call <see cref="SyncCharacterSnapshots"/> before any persistence when entries were added
    /// or removed via this dictionary.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Dictionary<string, CharacterStatProfileV2> CharacterStats
    {
        get
        {
            if (_characterStats is null)
            {
                _characterStats = new Dictionary<string, CharacterStatProfileV2>(StringComparer.OrdinalIgnoreCase);
                foreach (var snap in CharacterSnapshots)
                    _characterStats[snap.CharacterId] = snap;
            }
            return _characterStats;
        }
    }

    /// <summary>
    /// Rebuilds <see cref="CharacterSnapshots"/> from the runtime <see cref="CharacterStats"/>
    /// dictionary. Call this before persistence when the dictionary entries may have changed.
    /// </summary>
    public void SyncCharacterSnapshots()
    {
        if (_characterStats is null) return;
        CharacterSnapshots.Clear();
        CharacterSnapshots.AddRange(_characterStats.Values);
    }

    /// <summary>
    /// Rebuilds the runtime <see cref="CharacterStats"/> dictionary from the current
    /// <see cref="CharacterSnapshots"/> list. Call after a fresh load from the repository.
    /// </summary>
    public void RebuildCharacterStatsCache()
    {
        _characterStats = new Dictionary<string, CharacterStatProfileV2>(StringComparer.OrdinalIgnoreCase);
        foreach (var snap in CharacterSnapshots)
            _characterStats[snap.CharacterId] = snap;
    }

    public ThemeMachineSessionSnapshot? ThemeMachineSnapshot { get; set; }

    /// <summary>
    /// BeatCode of the current sub-beat cursor during Climax phase, e.g. "1a", "8g".
    /// Null when not in Climax phase.
    /// </summary>
    public string? CurrentBeatCode { get; set; }

    /// <summary>
    /// How many turns have elapsed in the current sub-beat since the cursor last advanced.
    /// Reset to 0 when CurrentBeatCode changes.
    /// </summary>
    public int TurnsInCurrentBeat { get; set; }

    // ---- V2 theme tracker -----------------------------------------------------------------
    /// <summary>Per-theme score state. Hydrated from <c>RolePlayV2ThemeScores</c>.</summary>
    public Dictionary<string, ThemeScoreState> ThemeScores { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Recent theme evidence ring. Hydrated from <c>RolePlayV2ThemeTrackerMeta.RecentEvidenceJson</c>.</summary>
    public List<ThemeEvidenceRecord> RecentEvidence { get; set; } = [];

    public string? PrimaryThemeId { get; set; }
    public string? SecondaryThemeId { get; set; }
    public string ThemeSelectionRule { get; set; } = "Top1";
    public DateTime ThemeTrackerUpdatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Number of user-initiated turns observed for this session. One increment per
    /// <c>StartTurnAsync</c> call.
    /// </summary>
    public int ObservedTurnCount { get; set; }

    /// <summary>Minimum number of turns that must be observed before theme selection commits.</summary>
    public int SelectionMinimumTurns { get; set; }

    // ---- Pacing / progression -------------------------------------------------------------
    public int CompletedScenarios { get; set; }
    public int InteractionsSinceCommitment { get; set; }
    public int InteractionsInApproaching { get; set; }
    public DateTime? ScenarioCommitmentTimeUtc { get; set; }

    // ---- Scenario history -----------------------------------------------------------------
    /// <summary>Rows hydrated from <c>RolePlayV2ScenarioHistory</c>.</summary>
    public List<ScenarioHistoryEntry> ScenarioHistory { get; set; } = [];

    // ---- Pairwise stats -------------------------------------------------------------------
    /// <summary>Rows hydrated from <c>RolePlayV2PairwiseStats</c>.</summary>
    public List<PairwiseStatRecord> PairwiseStats { get; set; } = [];

    // ---- Semantic telemetry ---------------------------------------------------------------
    public bool SemanticStepSucceeded { get; set; } = true;

    /// <summary>Recent semantic events. Hydrated from <c>RolePlayV2SemanticEvents</c>.</summary>
    public List<SemanticEventRecord> SemanticEvents { get; set; } = [];

    /// <summary>Theme delta breakdowns. Persisted in <c>RolePlayV2AdaptiveStates.SemanticDeltaBreakdownsJson</c>.</summary>
    public List<SemanticThemeDeltaBreakdown> SemanticDeltaBreakdowns { get; set; } = [];

    /// <summary>Stat delta breakdowns. Persisted in <c>RolePlayV2AdaptiveStates.SemanticStatDeltaBreakdownsJson</c>.</summary>
    public List<SemanticStatDeltaRecord> SemanticStatDeltaBreakdowns { get; set; } = [];

    // ---- Encounter summaries ---------------------------------------------------------------
    /// <summary>
    /// Per-character encounter summaries loaded from RolePlayV2EncounterSummaries.
    /// Populated at session load; updated in-memory when new summaries are written.
    /// </summary>
    public List<EncounterSummaryRecord> EncounterSummaries { get; set; } = [];
}

public sealed class CharacterLocationState
{
    public string CharacterId { get; set; } = string.Empty;
    public string? TrueLocation { get; set; }
    public bool IsHidden { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class CharacterLocationPerceptionState
{
    public string ObserverCharacterId { get; set; } = string.Empty;
    public string TargetCharacterId { get; set; } = string.Empty;
    public string? PerceivedLocation { get; set; }
    public int Confidence { get; set; }
    public bool HasLineOfSight { get; set; }
    public bool IsInProximity { get; set; }
    public string KnowledgeSource { get; set; } = "unknown";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
