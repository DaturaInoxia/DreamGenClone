using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Phase 1 round-trip verification (B-038): save an <see cref="AdaptiveScenarioState"/> with every
/// new V2 field populated, load it back, assert all fields survive persistence.
/// </summary>
public sealed class AdaptiveScenarioStateV2RoundTripTests
{
    [Fact]
    public async Task SaveAndLoad_PreservesAllPhase1Fields()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dreamgenclone-v2-roundtrip-{Guid.NewGuid():N}.db");
        try
        {
            var persistenceOptions = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath}" });
            var persistence = new SqlitePersistence(
                persistenceOptions,
                Options.Create(new LmStudioOptions()),
                Options.Create(new StoryAnalysisOptions()),
                Options.Create(new ScenarioAdaptationOptions()),
                NullLogger<SqlitePersistence>.Instance);
            await persistence.InitializeAsync();

            var repo = new RolePlayStateRepository(persistenceOptions);

            var sessionId = "session-rt-1";
            var nowUtc = new DateTime(2026, 5, 21, 12, 0, 0, DateTimeKind.Utc);

            var state = new AdaptiveScenarioState
            {
                SessionId = sessionId,
                ActiveScenarioId = "theme-1",
                ActiveVariantId = "variant-A",
                CurrentPhase = NarrativePhase.Climax,
                InteractionCountInPhase = 4,
                ConsecutiveLeadCount = 2,
                LastEvaluationUtc = nowUtc,
                CycleIndex = 3,
                ActiveFormulaVersion = "v1",
                SelectedWillingnessProfileId = "willing-default",
                SelectedNarrativeGateProfileId = "gates-default",
                CharacterEncounterProfileIds = new(StringComparer.OrdinalIgnoreCase) { ["char-a"] = "encounter-profile-1" },
                CurrentSceneLocation = "garden",
                CurrentBeatCode = "3b",
                TurnsInCurrentBeat = 2,

                // Phase 1 new columns
                CompletedScenarios = 5,
                InteractionsSinceCommitment = 9,
                InteractionsInApproaching = 1,
                ScenarioCommitmentTimeUtc = nowUtc.AddMinutes(-10),
                SemanticStepSucceeded = false,

                // Theme tracker (RolePlayV2ThemeTrackerMeta + RolePlayV2ThemeScores)
                PrimaryThemeId = "theme-1",
                SecondaryThemeId = "theme-2",
                ThemeSelectionRule = "Top2",
                ObservedTurnCount = 12,
                SelectionMinimumTurns = 3,
                ThemeTrackerUpdatedUtc = nowUtc,
                ThemeScores =
                {
                    ["theme-1"] = new ThemeScoreState
                    {
                        ThemeId = "theme-1",
                        ThemeName = "Theme One",
                        Score = 12.5,
                        Intensity = "Medium",
                        Blocked = false,
                        SuppressedHitCount = 1,
                        IsScenarioCandidate = true,
                        NarrativeFitScore = 0.71,
                        LastCandidateEvaluationTimeUtc = nowUtc.AddMinutes(-1),
                        CompletionCooldownInteractions = 2,
                        UpdatedUtc = nowUtc,
                        Breakdown = new ThemeScoreBreakdownV2
                        {
                            ChoiceSignal = 3.0,
                            CharacterStateSignal = 4.5,
                            InteractionEvidenceSignal = 2.0,
                            ScenarioPhaseSignal = 3.0
                        }
                    }
                },
                RecentEvidence =
                {
                    new ThemeEvidenceRecord
                    {
                        InteractionId = "interaction-99",
                        ThemeId = "theme-1",
                        SignalType = "interaction",
                        Delta = 1.25,
                        Confidence = 0.9,
                        Rationale = "explicit kiss",
                        CreatedUtc = nowUtc
                    }
                },

                // Pairwise stats (RolePlayV2PairwiseStats)
                PairwiseStats =
                {
                    new PairwiseStatRecord
                    {
                        SourceCharacterId = "char-a",
                        TargetCharacterId = "char-b",
                        Stats = { ["Desire"] = 7, ["Restraint"] = 2 },
                        UpdatedUtc = nowUtc
                    }
                },

                // Scenario history (RolePlayV2ScenarioHistory)
                ScenarioHistory =
                {
                    new ScenarioHistoryEntry
                    {
                        Id = "hist-1",
                        ScenarioId = "theme-1",
                        CompletedAtUtc = nowUtc.AddMinutes(-5),
                        InteractionCount = 8,
                        PeakThemeScore = 14,
                        PeakDesireLevel = 9,
                        AverageRestraintLevel = 3.5,
                        Notes = "completed beat 8g"
                    }
                },

                // Semantic events (RolePlayV2SemanticEvents)
                SemanticEvents =
                {
                    new SemanticEventRecord
                    {
                        InteractionId = "interaction-99",
                        EventId = "evt-kiss",
                        Confidence = 0.95m,
                        MappingId = "kiss->Desire",
                        Direction = "positive",
                        ThemeTargets = { "theme-1" },
                        ProcessedUtc = nowUtc
                    }
                },

                // Semantic delta breakdowns (JSON columns on parent row)
                SemanticDeltaBreakdowns =
                {
                    new SemanticThemeDeltaBreakdown
                    {
                        InteractionId = "interaction-99",
                        ThemeId = "theme-1",
                        SourceType = "semantic",
                        RawDelta = 1.5m,
                        AppliedDelta = 1.25m,
                        CappedDelta = 0.25m,
                        SuppressedDelta = 0m,
                        SuppressionReasonCode = null
                    }
                },
                SemanticStatDeltaBreakdowns =
                {
                    new SemanticStatDeltaRecord
                    {
                        InteractionId = "interaction-99",
                        CharacterId = "char-a",
                        StatName = "Desire",
                        SourceType = "semantic",
                        RawDelta = 2m,
                        AppliedDelta = 2m,
                        CappedDelta = 0m,
                        SuppressedDelta = 0m,
                        SuppressionReasonCode = null,
                        ReasonCode = "SemanticDeltaApplied"
                    }
                },

                // Character snapshots — exercise new Phase 1 fields
                CharacterSnapshots =
                {
                    new CharacterStatProfileV2
                    {
                        CharacterId = "char-a",
                        Desire = 8,
                        Restraint = 3,
                        Dominance = 4,
                        Loyalty = 7,
                        SelfRespect = 5,
                        SnapshotUtc = nowUtc,
                        UpdatedUtc = nowUtc,
                        BaselineStats = { ["Desire"] = 2, ["Restraint"] = 6 },
                        LastStatDeltas = { ["Desire"] = 2, ["Restraint"] = -1 },
                        LastStatDeltaUpdatedUtc = nowUtc,
                        RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 5, ["Connection"] = 6 }
                    }
                }
            };

            await repo.SaveAdaptiveStateAsync(state);
            var loaded = await repo.LoadAdaptiveStateAsync(sessionId);

            Assert.NotNull(loaded);

            // Parent row fields (Phase 1 new columns)
            Assert.Equal(5, loaded!.CompletedScenarios);
            Assert.Equal(9, loaded.InteractionsSinceCommitment);
            Assert.Equal(1, loaded.InteractionsInApproaching);
            Assert.Equal(nowUtc.AddMinutes(-10), loaded.ScenarioCommitmentTimeUtc);
            Assert.False(loaded.SemanticStepSucceeded);

            // Theme tracker meta
            Assert.Equal("theme-1", loaded.PrimaryThemeId);
            Assert.Equal("theme-2", loaded.SecondaryThemeId);
            Assert.Equal("Top2", loaded.ThemeSelectionRule);
            Assert.Equal(12, loaded.ObservedTurnCount);
            Assert.Equal(3, loaded.SelectionMinimumTurns);

            // Theme scores
            Assert.Single(loaded.ThemeScores);
            var theme = loaded.ThemeScores["theme-1"];
            Assert.Equal("Theme One", theme.ThemeName);
            Assert.Equal(12.5, theme.Score);
            Assert.Equal("Medium", theme.Intensity);
            Assert.Equal(1, theme.SuppressedHitCount);
            Assert.True(theme.IsScenarioCandidate);
            Assert.Equal(0.71, theme.NarrativeFitScore, 5);
            Assert.Equal(2, theme.CompletionCooldownInteractions);
            Assert.Equal(3.0, theme.Breakdown.ChoiceSignal);
            Assert.Equal(4.5, theme.Breakdown.CharacterStateSignal);
            Assert.Equal(2.0, theme.Breakdown.InteractionEvidenceSignal);
            Assert.Equal(3.0, theme.Breakdown.ScenarioPhaseSignal);

            // Recent evidence
            Assert.Single(loaded.RecentEvidence);
            Assert.Equal("interaction-99", loaded.RecentEvidence[0].InteractionId);
            Assert.Equal("explicit kiss", loaded.RecentEvidence[0].Rationale);

            // Pairwise stats
            Assert.Single(loaded.PairwiseStats);
            Assert.Equal("char-a", loaded.PairwiseStats[0].SourceCharacterId);
            Assert.Equal("char-b", loaded.PairwiseStats[0].TargetCharacterId);
            Assert.Equal(7, loaded.PairwiseStats[0].Stats["Desire"]);

            // Scenario history
            Assert.Single(loaded.ScenarioHistory);
            Assert.Equal("hist-1", loaded.ScenarioHistory[0].Id);
            Assert.Equal(8, loaded.ScenarioHistory[0].InteractionCount);
            Assert.Equal(14, loaded.ScenarioHistory[0].PeakThemeScore);
            Assert.Equal(3.5, loaded.ScenarioHistory[0].AverageRestraintLevel);
            Assert.Equal("completed beat 8g", loaded.ScenarioHistory[0].Notes);

            // Semantic events
            Assert.Single(loaded.SemanticEvents);
            Assert.Equal("evt-kiss", loaded.SemanticEvents[0].EventId);
            Assert.Equal(0.95m, loaded.SemanticEvents[0].Confidence);
            Assert.Equal("kiss->Desire", loaded.SemanticEvents[0].MappingId);
            Assert.Single(loaded.SemanticEvents[0].ThemeTargets);

            // Semantic delta breakdowns (JSON)
            Assert.Single(loaded.SemanticDeltaBreakdowns);
            Assert.Equal(1.25m, loaded.SemanticDeltaBreakdowns[0].AppliedDelta);
            Assert.Single(loaded.SemanticStatDeltaBreakdowns);
            Assert.Equal("SemanticDeltaApplied", loaded.SemanticStatDeltaBreakdowns[0].ReasonCode);

            // CharacterEncounterProfileIds (B-042 round-trip)
            Assert.Single(loaded.CharacterEncounterProfileIds);
            Assert.Equal("encounter-profile-1", loaded.CharacterEncounterProfileIds["char-a"]);

            // Character snapshots — Phase 1 new fields
            Assert.Single(loaded.CharacterSnapshots);
            var snap = loaded.CharacterSnapshots[0];
            Assert.Equal("char-a", snap.CharacterId);
            Assert.Equal(2, snap.BaselineStats["Desire"]);
            Assert.Equal(6, snap.BaselineStats["Restraint"]);
            Assert.Equal(2, snap.LastStatDeltas["Desire"]);
            Assert.Equal(-1, snap.LastStatDeltas["Restraint"]);
            Assert.Equal(nowUtc, snap.LastStatDeltaUpdatedUtc);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); } catch (IOException) { /* SQLite handle */ }
            }
        }
    }
}
