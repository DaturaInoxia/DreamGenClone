namespace DreamGenClone.Domain.RolePlay;

public sealed class AdaptiveScenarioState
{
    public string SessionId { get; set; } = string.Empty;
    public string? ActiveScenarioId { get; set; }
    public string? ActiveVariantId { get; set; }
    public NarrativePhase CurrentPhase { get; set; } = NarrativePhase.Opening;
    public int InteractionCountInPhase { get; set; }
    public int ConsecutiveLeadCount { get; set; }
    public DateTime LastEvaluationUtc { get; set; } = DateTime.UtcNow;
    public int CycleIndex { get; set; }
    public string ActiveFormulaVersion { get; set; } = string.Empty;
    public string? SelectedWillingnessProfileId { get; set; }
    public string? SelectedResistanceProfileId { get; set; }
    public string? SelectedNarrativeGateProfileId { get; set; }
    public int MotivationScore { get; set; }

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

        // Rebuild CharacterRoles from the persisted CharacterRole field on each snapshot.
        CharacterRoles.Clear();
        foreach (var snap in CharacterSnapshots)
        {
            if (!string.IsNullOrWhiteSpace(snap.CharacterId) && !string.IsNullOrWhiteSpace(snap.CharacterRole))
                CharacterRoles[snap.CharacterId] = snap.CharacterRole;
        }

        // Seed RuntimeEncounterStats at baseline 50 for any character that has a role but
        // no encounter stats yet. This ensures the behavioral dimensions panel is populated
        // on session load rather than waiting for the first semantic stat mutation.
        foreach (var snap in CharacterSnapshots)
        {
            if (!string.IsNullOrWhiteSpace(snap.CharacterRole)
                && snap.RuntimeEncounterStats is not { Count: > 0 })
            {
                var dims = DreamGenClone.Domain.StoryAnalysis.BehavioralDimensionCatalog.GetDimensions(snap.CharacterRole);
                if (dims.Count > 0)
                    snap.RuntimeEncounterStats = dims.ToDictionary(d => d.Name, _ => 50, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public ThemeMachineSessionSnapshot? ThemeMachineSnapshot { get; set; }

    /// <summary>
    /// Maps characterId (actorKey) → role label (e.g. "Wife", "Husband", "OtherMan").
    /// Used by StatToDimensionMappings to look up drift rules during stat mutation.
    /// Populated by RebindEncounterProfile (T024) when encounter profiles are bound.
    /// Entries missing from this dict result in a no-op drift (empty rule set).
    /// </summary>
    public Dictionary<string, string> CharacterRoles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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

    // ---- Universal encounter tracking ----------------------------------------------------
    // Active across all phases, not just Climax. Encounters start on first sexual content,
    // end on keyword/LLM completion detection. GlobalEncounterCount is cumulative for the
    // session (never decremented); CurrentEncounterNumber tracks the currently active
    // encounter (0 = no active encounter).

    /// <summary>
    /// Cumulative count of ALL completed encounters in this session.
    /// Incremented on every encounter boundary detection (any phase, any marker).
    /// Never decremented. Persisted in DB.
    /// </summary>
    public int GlobalEncounterCount { get; set; }

    // ---- Multi-encounter Climax state -----------------------------------------------------
    // CurrentEncounterNumber is now repurposed as a universal active-encounter tracker.
    // 0 = no active encounter (inactive/dormant). When non-zero, an encounter is in progress
    // and tracked across all phases. Multi-encounter Climax uses GlobalEncounterCount + 1
    // for numbering instead of hardcoded value 1.
    /// <summary>
    /// 1-based index of the currently active encounter, universal across all phases.
    /// 0 = no active encounter (dormant). Set to GlobalEncounterCount + 1 on first
    /// sexual content in any phase, or on Climax entry for multi-encounter themes.
    /// Set to 0 when the encounter boundary fires (encounter no longer active)
    /// or when leaving Climax / entering Reset.
    /// </summary>
    public int CurrentEncounterNumber { get; set; }

    /// <summary>
    /// Number of interactions generated in the current encounter since it started.
    /// Reset to 0 when CurrentEncounterNumber advances.
    /// </summary>
    public int InteractionsInCurrentEncounter { get; set; }

    /// <summary>
    /// Current phase of the two-phase multi-encounter time-skip:
    /// None = no time-skip pending (normal flow).
    /// CloseScene = "Close the current encounter naturally." directive pending.
    /// AdvanceTime = "Advance time to a new moment..." directive pending.
    /// Set to CloseScene when TryDetectEncounterBoundaryAsync advances CurrentEncounterNumber.
    /// Transitioned by the overflow block in ContinueAsAsync.
    /// Dormant (None) for all themes without [ClimaxMode:multi-encounter].
    /// </summary>
    public TimeSkipPhase CurrentTimeSkipPhase { get; set; }

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

    // ---- Encounter participation tracking ---------------------------------------------------
    /// <summary>
    /// Per-character encounter participation state. Keyed by character name (actorName).
    /// Tracks whether each character is actively in a sexual encounter, used by boundary
    /// detection, prompt building, gates, and other modules. Sync (heuristic) and async
    /// (LLM-confirmed) tiers.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Dictionary<string, CharacterEncounterState> CharacterEncounterStates { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Set to true when state changes (character stats, theme scores, non-time-skip
    /// phase transitions, location state) are made in-memory but not yet persisted.
    /// Time-skip mutations (CurrentEncounterNumber, CurrentTimeSkipPhase, etc.)
    /// persist synchronously via B-057 and do NOT use this dirty flag.
    /// Flushed at turn completion on success; discarded on turn failure.
    /// Not serialized — always starts false on load.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsStateDirty { get; set; }

    // ---- Last encounter evidence span (B-056 aftermath closure) ---------------------------
    // B-058 Phase 5.3: LastEncounterEvidenceSpan has been REMOVED from the in-memory state.
    // The husband-aftermath contrast directive now reads from the most recent
    // EncounterCompletion summary (LlmSummary ?? TemplateSummary ?? DetectionEvidence)
    // via HusbandAftermathInjector. The DB column `LastEncounterEvidenceSpan TEXT` is kept
    // for backward compatibility with existing rows but no longer written or read.

    // ---- B-058 encounter interaction-range tracking (runtime only, not persisted) ---------
    // B-058 writes an EncounterCompletion summary at every encounter-boundary detection.
    // The summary captures the interaction-list index range of the encounter so async LLM
    // enrichment can load the actual interactions in that range (not TakeLast(30)).
    // These fields are runtime-only — they reconstruct on session reload from
    // GlobalEncounterCount / CurrentEncounterNumber state (B-057) and the persisted
    // EncounterSummaryRecord rows.
    /// <summary>
    /// Index into <c>RolePlaySession.Interactions</c> of the first interaction in the
    /// currently active encounter (inclusive). Set when an encounter starts:
    /// 1st encounter = Climax entry (or first sexual content in non-Climax); 2nd+ =
    /// AdvanceTime → None transition. [JsonIgnore] — not persisted.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int CurrentEncounterStartInteractionIndex { get; set; }

    /// <summary>
    /// Staging field for the inclusive ending interaction index of the encounter that just
    /// completed detection. Set at detection time (= <c>Interactions.Count - 1</c>) before
    /// the synchronous EncounterCompletion record is written. [JsonIgnore] — not persisted.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int LastEncounterEndInteractionIndex { get; set; }

    // ---- Encounter participation helper -----------------------------------------------------
    /// <summary>
    /// Returns true if the named character is flagged as actively in an encounter
    /// (sync heuristic tier). Safe to call from any module.
    /// </summary>
    public bool IsCharacterHavingSex(string characterName)
    {
        return CharacterEncounterStates.TryGetValue(characterName, out var state)
            && state.IsHavingSex;
    }
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

/// <summary>
/// Per-character encounter participation state. Two-tier: sync heuristic (IsHavingSex)
/// is available immediately for boundary detection and prompts; async LLM-confirmed
/// (IsHavingSexConfirmed) is authoritative for scoring and analytics.
/// </summary>
public sealed class CharacterEncounterState
{
    /// <summary>Sync heuristic — true when character interaction contains sexual/erotic content.</summary>
    public bool IsHavingSex { get; set; }

    /// <summary>Async LLM-confirmed — true when the semantic job detects active-in-encounter.</summary>
    public bool IsHavingSexConfirmed { get; set; }

    /// <summary>Which encounter number this participation belongs to.</summary>
    public int EncounterNumber { get; set; }

    /// <summary>UTC timestamp when the character entered the encounter.</summary>
    public DateTime EnteredUtc { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp when this encounter state was last updated.</summary>
    public DateTime? EnteredEncounterUtc { get; set; }
}

/// <summary>
/// Time-skip state for multi-encounter Climax / aftermath transitions.
/// Primary flow (multi-encounter only): None → CloseScene → AdvanceTime → None.
/// With aftermath marker: CloseScene transitions to AftermathCoupleInteraction
/// (then to AdvanceTime → None). Aftermath-only (no multi-encounter marker):
/// None → AftermathCoupleInteraction → None.
/// </summary>
public enum TimeSkipPhase
{
    /// <summary>No time-skip pending — normal continuation flow.</summary>
    None = 0,
    /// <summary>Close-scene directive pending. Will inject closure directive.</summary>
    CloseScene = 1,
    /// <summary>Advance-time directive pending. Will inject "Advance time to a new moment..."</summary>
    AdvanceTime = 2,
    /// <summary>
    /// Wife-husband aftermath closure directive pending (B-056 [Aftermath:husband-contrast]).
    /// Fires between CloseScene and AdvanceTime in multi-encounter Climax flows,
    /// or standalone in any non-Reset phase where the aftermath marker is present.
    /// The wife gets dressed, returns to the normal setting, and interacts with
    /// her husband while concealing what happened.
    /// </summary>
    AftermathCoupleInteraction = 3
}
