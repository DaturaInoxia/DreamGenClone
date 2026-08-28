using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using static DreamGenClone.Tests.RolePlay.RolePlayTestFactory;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayAdaptiveStateServiceTests
{
    [Fact]
    public async Task UpdateFromInteractionAsync_ProjectsSemanticTelemetry_IntoDebugEventMetadata()
    {
        var debugSink = new RecordingDebugSink();
        var service = new RolePlayAdaptiveStateService(
            new FakeThemeCatalogService(),
            debugSink,
            NullLogger<RolePlayAdaptiveStateService>.Instance);

        var session = new RolePlaySession
        {
            AdaptiveState = new AdaptiveScenarioState()
        };

        await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            Id = "ix-semantic-telemetry",
            ActorName = "Becky",
            Content = "no semantic markers in this interaction"
        });

        var record = Assert.Single(debugSink.Records);
        using var metadataDoc = JsonDocument.Parse(record.MetadataJson);
        var root = metadataDoc.RootElement;

        Assert.True(root.TryGetProperty("semanticStepSucceeded", out var semanticStepSucceeded));
        Assert.True(semanticStepSucceeded.GetBoolean());

        Assert.True(root.TryGetProperty("semanticEvents", out var semanticEvents));
        Assert.Equal(JsonValueKind.Array, semanticEvents.ValueKind);

        Assert.True(root.TryGetProperty("semanticDeltaBreakdowns", out var semanticDeltaBreakdowns));
        Assert.Equal(JsonValueKind.Array, semanticDeltaBreakdowns.ValueKind);

        Assert.True(root.TryGetProperty("semanticStatDeltaBreakdowns", out var semanticStatDeltaBreakdowns));
        Assert.Equal(JsonValueKind.Array, semanticStatDeltaBreakdowns.ValueKind);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_FailsFast_WhenSemanticPayloadPresentAndSemanticConfigSourceMissing()
    {
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService());
        var session = new RolePlaySession
        {
            AdaptiveState = new AdaptiveScenarioState()
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateFromInteractionAsync(
            session,
            new RolePlayInteraction
            {
                ActorName = "Becky",
                Content = "[[semantic:betrayal:0.75]]"
            }));

        Assert.Contains(DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.MissingSemanticConfiguration, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(session.AdaptiveState.SemanticStepSucceeded);
        Assert.Empty(session.AdaptiveState.SemanticEvents);
        Assert.Empty(session.AdaptiveState.SemanticDeltaBreakdowns);
        Assert.Empty(session.AdaptiveState.SemanticStatDeltaBreakdowns);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_SelectsTop2Blend_WhenTopThemesAreClose()
    {
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService());
        var session = new RolePlaySession();

        var interaction = new RolePlayInteraction
        {
            ActorName = "Alex",
            Content = "I want control and command, but there is danger, risk, secret heat and control again."
        };

        var state = await service.UpdateFromInteractionAsync(session, interaction);

        Assert.Equal("Top2Blend", state.ThemeSelectionRule);
        Assert.False(string.IsNullOrWhiteSpace(state.PrimaryThemeId));
        Assert.False(string.IsNullOrWhiteSpace(state.SecondaryThemeId));
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_DoesNotTrackNarrativeAsCharacterStats()
    {
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService());
        var session = new RolePlaySession();

        var interaction = new RolePlayInteraction
        {
            ActorName = "Narrative",
            InteractionType = InteractionType.System,
            Content = "The scene grows warmer and closer with trust and comfort."
        };

        var state = await service.UpdateFromInteractionAsync(session, interaction);

        Assert.False(state.CharacterStats.ContainsKey("Narrative"));
        Assert.Equal("Top2Blend", state.ThemeSelectionRule);
        Assert.False(string.IsNullOrWhiteSpace(state.SecondaryThemeId));
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_RemovesNonCanonicalStatKeys()
    {
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService());
        var session = new RolePlaySession
        {
            AdaptiveState = new AdaptiveScenarioState
            {
                CharacterSnapshots = [new CharacterStatProfileV2
                    {
                        CharacterId = "becky",
                            Desire = 50,
                            Restraint = 50,
                            Dominance = 50,
                            Loyalty = 50,
                            SelfRespect = 50,
                            RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 50, ["Connection"] = 50 }
                    }]
            }
        };

        var state = await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Becky",
            Content = "A calm line with no special influence."
        });

        var stats = CharacterStatProfileV2Accessor.GetAllStats(state.CharacterStats["Becky"]);
        Assert.False(stats.ContainsKey("Husband Connection"));
        Assert.False(stats.ContainsKey("Wife Desire"));
        Assert.All(AdaptiveStatCatalog.CanonicalStatNames, stat => Assert.True(stats.ContainsKey(stat)));
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_SuppressesThemeAffinityStatDeltas_InBuildUp()
    {
        var service = new RolePlayAdaptiveStateService(new PolicyThemeCatalogService());
        var session = new RolePlaySession
        {
            AdaptiveState = new AdaptiveScenarioState
            {
                CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp,
                CharacterSnapshots = [CharacterStatProfileV2Accessor.CreateDefault("becky")]
            }
        };

        var state = await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Becky",
            Content = "party people music"
        });

        Assert.Equal(AdaptiveStatCatalog.DefaultValue, state.CharacterStats["Becky"].Desire);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_AppliesOnlyTopThemeAffinity_InCommitted()
    {
        var service = new RolePlayAdaptiveStateService(new PolicyThemeCatalogService());
        var session = new RolePlaySession
        {
            AdaptiveState = new AdaptiveScenarioState
            {
                CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Committed,
                CharacterSnapshots = [CharacterStatProfileV2Accessor.CreateDefault("becky")]
            }
        };

        var state = await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Becky",
            Content = "party people music"
        });

        // With top-1 theme affinity + committed phase cap(1), Desire should only move by +1.
        Assert.Equal(AdaptiveStatCatalog.DefaultValue + 1, state.CharacterStats["Becky"].Desire);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_AppliesEarlyTurnPerStatAndGlobalBudgetCaps()
    {
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService());
        var session = new RolePlaySession
        {
            AdaptiveState = new AdaptiveScenarioState
            {
                CharacterSnapshots = [CharacterStatProfileV2Accessor.CreateDefault("becky")]
            }
        };

        var state = await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Becky",
            Content = string.Join(' ', Enumerable.Repeat("kiss touch desire want close heat can't wrong shouldn't hesitate guilt fear caught risk panic nervous safe comfort trust reassure control command obey claim choose decide insist husband wife promise vow faithful devoted commitment boundary boundaries respect dignity self-worth walk away no", 8))
        });

        var stats = CharacterStatProfileV2Accessor.GetAllStats(state.CharacterStats["Becky"]);
        var deltas = AdaptiveStatCatalog.CanonicalStatNames
            .Select(statName => stats[statName] - AdaptiveStatCatalog.DefaultValue)
            .ToList();

        Assert.All(deltas, delta => Assert.InRange(Math.Abs(delta), 0, 2));
        Assert.InRange(deltas.Sum(delta => Math.Abs(delta)), 0, 10);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_SuppressesAdjacentRepeatedSemanticEvent_ByCooldown()
    {
        var rpThemeService = new SemanticRpThemeService(
            new Dictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticEventMapping>>(StringComparer.OrdinalIgnoreCase)
            {
                ["betrayal"] =
                [
                    new DreamGenClone.Domain.RolePlay.RPSemanticEventMapping
                    {
                        EventId = "betrayal",
                        ThemeId = "theme-corruption",
                        Delta = 2.0m,
                        ConfidenceMin = 0m,
                        ConfidenceMax = 1m,
                        Direction = "increase",
                        ReasonCode = "semantic-betrayal"
                    }
                ]
            },
            new Dictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticStatMapping>>(StringComparer.OrdinalIgnoreCase)
            {
                ["betrayal"] =
                [
                    new DreamGenClone.Domain.RolePlay.RPSemanticStatMapping
                    {
                        EventId = "betrayal",
                        ThemeId = "theme-corruption",
                        TargetStat = "Desire",
                        Delta = 2m,
                        ConfidenceMin = 0m,
                        ConfidenceMax = 1m,
                        ReasonCode = "semantic-betrayal-stat"
                    }
                ]
            });

        var service = new RolePlayAdaptiveStateService(
            new SemanticThemeCatalogService(),
            new EmptyThemePreferenceService(),
            rpThemeService,
            statKeywordCategoryService: null,
            new NullSteeringProfileService(),
            new RecordingDebugSink(),
            NullLogger<RolePlayAdaptiveStateService>.Instance);

        var session = new RolePlaySession
        {
            SelectedRPThemeProfileId = "profile-semantic",
            AdaptiveState = new AdaptiveScenarioState
            {
                ThemeScores = new Dictionary<string, ThemeScoreState>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["theme-corruption"] = new ThemeScoreState
                        {
                            ThemeId = "theme-corruption",
                            ThemeName = "Corruption",
                            Score = 10,
                            Breakdown = new ThemeScoreBreakdownV2()
                        }
                    }
            }
        };

        var interaction1 = new RolePlayInteraction
        {
            Id = "semantic-1",
            ActorName = "Becky",
            Content = "[[semantic:betrayal:0.8]]"
        };
        session.Interactions.Add(interaction1);
        await service.UpdateFromInteractionAsync(session, interaction1);

        var interaction2 = new RolePlayInteraction
        {
            Id = "semantic-2",
            ActorName = "Becky",
            Content = "[[semantic:betrayal:0.8]]"
        };
        session.Interactions.Add(interaction2);
        var updated = await service.UpdateFromInteractionAsync(session, interaction2);

        var lastBreakdown = updated.SemanticDeltaBreakdowns.LastOrDefault();
        if (lastBreakdown is not null)
        {
            // Theme-scoring pass only runs while no primary theme is committed; if it ran
            // and produced a breakdown, it must be a cooldown-suppressed one.
            Assert.Equal(DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.SemanticSuppressedAdjacentCooldown, lastBreakdown.SuppressionReasonCode);
            Assert.Equal(0m, lastBreakdown.AppliedDelta);
            Assert.Equal(2.0m, lastBreakdown.SuppressedDelta);
        }
        // Stat-mapping pass always runs; cooldown suppression must be applied there.
        var lastStatBreakdown = Assert.Single(updated.SemanticStatDeltaBreakdowns);
        Assert.Equal(DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.SemanticSuppressedAdjacentCooldown, lastStatBreakdown.SuppressionReasonCode);
        Assert.Equal(0m, lastStatBreakdown.AppliedDelta);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_ZeroesBlockedTheme_WhenSemanticEvidenceTargetsLockedTheme()
    {
        var rpThemeService = new SemanticRpThemeService(
            new Dictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticEventMapping>>(StringComparer.OrdinalIgnoreCase)
            {
                ["betrayal"] =
                [
                    new DreamGenClone.Domain.RolePlay.RPSemanticEventMapping
                    {
                        EventId = "betrayal",
                        ThemeId = "theme-corruption",
                        Delta = 3m,
                        ConfidenceMin = 0m,
                        ConfidenceMax = 1m,
                        Direction = "increase",
                        ReasonCode = "semantic-betrayal"
                    }
                ]
            },
            new Dictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticStatMapping>>(StringComparer.OrdinalIgnoreCase)
            {
                ["betrayal"] =
                [
                    new DreamGenClone.Domain.RolePlay.RPSemanticStatMapping
                    {
                        EventId = "betrayal",
                        ThemeId = "theme-corruption",
                        TargetStat = "Desire",
                        Delta = 2m,
                        ConfidenceMin = 0m,
                        ConfidenceMax = 1m,
                        ReasonCode = "semantic-betrayal-stat"
                    }
                ]
            });

        var service = new RolePlayAdaptiveStateService(
            new SemanticThemeCatalogService(),
            new EmptyThemePreferenceService(),
            rpThemeService,
            statKeywordCategoryService: null,
            new NullSteeringProfileService(),
            new RecordingDebugSink(),
            NullLogger<RolePlayAdaptiveStateService>.Instance);

        var session = new RolePlaySession
        {
            SelectedRPThemeProfileId = "profile-semantic",
            AdaptiveState = new AdaptiveScenarioState
            {
                ThemeScores = new Dictionary<string, ThemeScoreState>(StringComparer.OrdinalIgnoreCase)
                {
                    ["theme-corruption"] = new ThemeScoreState
                    {
                        ThemeId = "theme-corruption",
                        ThemeName = "Corruption",
                        Score = 40,
                        Blocked = true,
                        Breakdown = new ThemeScoreBreakdownV2 { InteractionEvidenceSignal = 12 }
                    }
                }
            }
        };

        var interaction = new RolePlayInteraction
        {
            Id = "semantic-blocked",
            ActorName = "Becky",
            Content = "[[semantic:betrayal:0.9]]"
        };
        session.Interactions.Add(interaction);

        var updated = await service.UpdateFromInteractionAsync(session, interaction);

        Assert.Equal(0d, updated.ThemeScores["theme-corruption"].Score);
        Assert.Equal(0d, updated.ThemeScores["theme-corruption"].Breakdown.InteractionEvidenceSignal);
        var breakdown = Assert.Single(updated.SemanticDeltaBreakdowns);
        Assert.Equal(DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.SemanticSuppressedThemeBlocked, breakdown.SuppressionReasonCode);
        Assert.Equal(0m, breakdown.AppliedDelta);
        var statBreakdown = Assert.Single(updated.SemanticStatDeltaBreakdowns);
        Assert.Equal(DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.SemanticSuppressedThemeBlocked, statBreakdown.SuppressionReasonCode);
        Assert.Equal(0m, statBreakdown.AppliedDelta);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_SoftSkips_WhenSemanticEventHasNoStatMappingConfigured()
    {
        // Per product rule: when a semantic event has a theme-mapping but no stat-mapping
        // configured, the stat-mapping pass is simply not configured for that event id.
        // This is not an error — the rp session continues normally and the theme-mapping
        // contribution is still applied.
        var rpThemeService = new SemanticRpThemeService(
            new Dictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticEventMapping>>(StringComparer.OrdinalIgnoreCase)
            {
                ["betrayal"] =
                [
                    new DreamGenClone.Domain.RolePlay.RPSemanticEventMapping
                    {
                        EventId = "betrayal",
                        ThemeId = "theme-corruption",
                        Delta = 1m,
                        ConfidenceMin = 0m,
                        ConfidenceMax = 1m,
                        Direction = "increase",
                        ReasonCode = "semantic-betrayal"
                    }
                ]
            });

        var service = new RolePlayAdaptiveStateService(
            new SemanticThemeCatalogService(),
            new EmptyThemePreferenceService(),
            rpThemeService,
            statKeywordCategoryService: null,
            new NullSteeringProfileService(),
            new RecordingDebugSink(),
            NullLogger<RolePlayAdaptiveStateService>.Instance);

        var session = new RolePlaySession
        {
            SelectedRPThemeProfileId = "profile-semantic",
            AdaptiveState = new AdaptiveScenarioState
            {
                ThemeScores = new Dictionary<string, ThemeScoreState>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["theme-corruption"] = new ThemeScoreState
                        {
                            ThemeId = "theme-corruption",
                            ThemeName = "Corruption",
                            Score = 5,
                            Breakdown = new ThemeScoreBreakdownV2()
                        }
                    }
            }
        };

        var interaction = new RolePlayInteraction
        {
            Id = "semantic-no-stat-map",
            ActorName = "Becky",
            Content = "[[semantic:betrayal:0.7]]"
        };

        await service.UpdateFromInteractionAsync(session, interaction);

        Assert.True(session.AdaptiveState.SemanticStepSucceeded);
        Assert.Empty(session.AdaptiveState.SemanticStatDeltaBreakdowns);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_SoftSkips_WhenSemanticEventIdIsUnknown()
    {
        // Per product rule: a semantic marker whose event id has no mapping configured is
        // not an error — that event simply has no behavior configured and the session
        // continues normally.
        var rpThemeService = new SemanticRpThemeService(new Dictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticEventMapping>>(StringComparer.OrdinalIgnoreCase)
        {
            ["known-event"] =
            [
            new DreamGenClone.Domain.RolePlay.RPSemanticEventMapping
                {
                    EventId = "known-event",
                    ThemeId = "theme-corruption",
                    Delta = 1m,
                    ConfidenceMin = 0m,
                    ConfidenceMax = 1m,
                    Direction = "increase",
                    ReasonCode = "semantic-known"
                }
            ]
        });

        var service = new RolePlayAdaptiveStateService(
            new SemanticThemeCatalogService(),
            new EmptyThemePreferenceService(),
            rpThemeService,
            statKeywordCategoryService: null,
            new NullSteeringProfileService(),
            new RecordingDebugSink(),
            NullLogger<RolePlayAdaptiveStateService>.Instance);

        var session = new RolePlaySession
        {
            SelectedRPThemeProfileId = "profile-semantic",
            AdaptiveState = new AdaptiveScenarioState
            {
                ThemeScores = new Dictionary<string, ThemeScoreState>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["theme-corruption"] = new ThemeScoreState
                        {
                            ThemeId = "theme-corruption",
                            ThemeName = "Corruption",
                            Score = 5,
                            Breakdown = new ThemeScoreBreakdownV2()
                        }
                    }
            }
        };

        var interaction = new RolePlayInteraction
        {
            Id = "semantic-unknown",
            ActorName = "Becky",
            Content = "[[semantic:unknown-event:0.7]]"
        };

        await service.UpdateFromInteractionAsync(session, interaction);

        Assert.True(session.AdaptiveState.SemanticStepSucceeded);
        Assert.Empty(session.AdaptiveState.SemanticDeltaBreakdowns);
        Assert.Empty(session.AdaptiveState.SemanticStatDeltaBreakdowns);
    }

    // ── B-078 follow-up: OtherMan seduction-trope events target the Wife ────────

    private static (RolePlayAdaptiveStateService Service, RolePlaySession Session) CreateInferredTargetFixture(
        string targetStat = "Loyalty",
        string eventId = "otherman-charmer",
        decimal delta = 2m,
        bool includeWifeInRoles = true)
    {
        var rpThemeService = new SemanticRpThemeService(
            new Dictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticEventMapping>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticStatMapping>>(StringComparer.OrdinalIgnoreCase)
            {
                [eventId] =
                [
                    new DreamGenClone.Domain.RolePlay.RPSemanticStatMapping
                    {
                        EventId = eventId,
                        ThemeId = "theme-corruption",
                        TargetStat = targetStat,
                        Delta = delta,
                        Direction = "decrease",
                        ConfidenceMin = 0m,
                        ConfidenceMax = 1m,
                        ReasonCode = "otherman-trope-stat"
                    }
                ]
            });

        var service = new RolePlayAdaptiveStateService(
            new SemanticThemeCatalogService(),
            new EmptyThemePreferenceService(),
            rpThemeService,
            statKeywordCategoryService: null,
            new NullSteeringProfileService(),
            new RecordingDebugSink(),
            NullLogger<RolePlayAdaptiveStateService>.Instance);

        var session = new RolePlaySession
        {
            Id = Guid.NewGuid().ToString(),
            PersonaName = "Ken",
            SelectedRPThemeProfileId = "profile-semantic",
            AdaptiveState = new AdaptiveScenarioState
            {
                ThemeScores = new Dictionary<string, ThemeScoreState>(StringComparer.OrdinalIgnoreCase)
                {
                    ["theme-corruption"] = new ThemeScoreState
                    {
                        ThemeId = "theme-corruption",
                        ThemeName = "Corruption",
                        Score = 10,
                        Breakdown = new ThemeScoreBreakdownV2()
                    }
                },
                CharacterRoles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Becky"] = "Wife",
                    ["Dean"] = "OtherMan"
                }
            }
        };

        // CharacterStats is a computed read-only property backed by CharacterSnapshots.
        _ = session.AdaptiveState.CharacterStats; // initialize the runtime cache
        session.AdaptiveState.CharacterStats["Becky"] = new CharacterStatProfileV2 { CharacterId = "Becky", CharacterRole = "Wife", Loyalty = 50, Restraint = 50 };
        session.AdaptiveState.CharacterStats["Dean"] = new CharacterStatProfileV2 { CharacterId = "Dean", CharacterRole = "OtherMan", Loyalty = 50, Restraint = 50 };

        if (!includeWifeInRoles)
        {
            session.AdaptiveState.CharacterRoles.Remove("Becky");
        }

        return (service, session);
    }

    [Fact]
    public async Task ApplyInferredSemanticEvidence_OtherManEventWithWifeTarget_AppliesDeltaToWife()
    {
        var (service, session) = CreateInferredTargetFixture();
        var interaction = new RolePlayInteraction
        {
            Id = "otherman-1",
            ActorName = "Dean",
            Content = "Dean helps Becky with the firewood, a warm hand on hers."
        };

        var updated = await service.ApplyInferredSemanticEvidenceAsync(
            session, interaction,
            [new IRolePlayAdaptiveStateService.InferredSemanticSignal("otherman-charmer", 0.8m, "Dean", "Becky", "helping with firewood")]);

        // B-078: the LLM's targetCharacterName (Wife) is now honored for stat deltas.
        Assert.Equal(48, updated.CharacterStats["Becky"].Loyalty);
        Assert.Equal(50, updated.CharacterStats["Dean"].Loyalty);
    }

    [Fact]
    public async Task ApplyInferredSemanticEvidence_OtherManEventWithoutTarget_ResolvesWifeDeterministically()
    {
        var (service, session) = CreateInferredTargetFixture();
        var interaction = new RolePlayInteraction
        {
            Id = "otherman-2",
            ActorName = "Dean",
            Content = "Dean is warm, confident, just there."
        };

        // No targetCharacterName — the engine must resolve the Wife from CharacterRoles.
        var updated = await service.ApplyInferredSemanticEvidenceAsync(
            session, interaction,
            [new IRolePlayAdaptiveStateService.InferredSemanticSignal("otherman-charmer", 0.8m, "Dean", null, null)]);

        Assert.Equal(48, updated.CharacterStats["Becky"].Loyalty);
        Assert.Equal(50, updated.CharacterStats["Dean"].Loyalty);
    }

    [Fact]
    public async Task ApplyInferredSemanticEvidence_NonOtherManEventWithoutTarget_AppliesDeltaToActor()
    {
        var (service, session) = CreateInferredTargetFixture(eventId: "emotional-surrender");
        var interaction = new RolePlayInteraction
        {
            Id = "otherman-3",
            ActorName = "Dean",
            Content = "Dean feels the pull of something forbidden."
        };

        // Not an otherman-* event and no target — the actor (Dean) is the target (unchanged behavior).
        var updated = await service.ApplyInferredSemanticEvidenceAsync(
            session, interaction,
            [new IRolePlayAdaptiveStateService.InferredSemanticSignal("emotional-surrender", 0.8m, "Dean", null, null)]);

        Assert.Equal(50, updated.CharacterStats["Becky"].Loyalty);
        Assert.Equal(48, updated.CharacterStats["Dean"].Loyalty);
    }

    [Fact]
    public async Task ApplyInferredSemanticEvidence_OtherManEventNoWifeInState_KeepsActorTarget()
    {
        var (service, session) = CreateInferredTargetFixture(includeWifeInRoles: false);
        var interaction = new RolePlayInteraction
        {
            Id = "otherman-4",
            ActorName = "Dean",
            Content = "Dean watches the embers."
        };

        // No Wife resolvable — deterministic redirect is a no-op; actor fallback applies.
        var updated = await service.ApplyInferredSemanticEvidenceAsync(
            session, interaction,
            [new IRolePlayAdaptiveStateService.InferredSemanticSignal("otherman-charmer", 0.8m, "Dean", null, null)]);

        Assert.Equal(48, updated.CharacterStats["Dean"].Loyalty);
    }

    // ── Semantic stat per-turn cap + final-band damping ─────────────────────

    private static (RolePlayAdaptiveStateService Service, RolePlaySession Session) CreateSemanticCapFixture(
        StoryAnalysisOptions options,
        string targetStat,
        decimal delta,
        string direction,
        int initialValue,
        string eventId = "semantic-cap-event")
    {
        var rpThemeService = new SemanticRpThemeService(
            new Dictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticEventMapping>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticStatMapping>>(StringComparer.OrdinalIgnoreCase)
            {
                [eventId] =
                [
                    new DreamGenClone.Domain.RolePlay.RPSemanticStatMapping
                    {
                        EventId = eventId,
                        ThemeId = "theme-corruption",
                        TargetStat = targetStat,
                        Delta = delta,
                        Direction = direction,
                        ConfidenceMin = 0m,
                        ConfidenceMax = 1m,
                        ReasonCode = "cap-test-stat"
                    }
                ]
            });

        var service = new RolePlayAdaptiveStateService(
            new SemanticThemeCatalogService(),
            new EmptyThemePreferenceService(),
            rpThemeService,
            statKeywordCategoryService: null,
            new NullSteeringProfileService(),
            new RecordingDebugSink(),
            NullLogger<RolePlayAdaptiveStateService>.Instance,
            intensityProfileService: null,
            storyAnalysisOptions: Options.Create(options));

        var session = new RolePlaySession
        {
            Id = Guid.NewGuid().ToString(),
            PersonaName = "Ken",
            SelectedRPThemeProfileId = "profile-semantic",
            AdaptiveState = new AdaptiveScenarioState
            {
                ThemeScores = new Dictionary<string, ThemeScoreState>(StringComparer.OrdinalIgnoreCase)
                {
                    ["theme-corruption"] = new ThemeScoreState
                    {
                        ThemeId = "theme-corruption",
                        ThemeName = "Corruption",
                        Score = 10,
                        Breakdown = new ThemeScoreBreakdownV2()
                    }
                },
                CharacterRoles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Becky"] = "Wife",
                    ["Dean"] = "OtherMan"
                }
            }
        };

        _ = session.AdaptiveState.CharacterStats; // initialize the runtime cache
        session.AdaptiveState.CharacterStats["Becky"] = new CharacterStatProfileV2
        {
            CharacterId = "Becky",
            CharacterRole = "Wife",
            Desire = 50,
            Restraint = 50,
            Loyalty = 50,
            SelfRespect = 50,
            Dominance = 50
        };
        CharacterStatProfileV2Accessor.SetStat(session.AdaptiveState.CharacterStats["Becky"], targetStat, initialValue);

        return (service, session);
    }

    private static Task<AdaptiveScenarioState> ApplySemanticCapSignalAsync(
        RolePlayAdaptiveStateService service,
        RolePlaySession session,
        string eventId,
        string targetCharacterName = "Becky")
    {
        var interaction = new RolePlayInteraction
        {
            Id = $"cap-{Guid.NewGuid():N}",
            ActorName = "Dean",
            Content = "trigger"
        };
        return service.ApplyInferredSemanticEvidenceAsync(
            session, interaction,
            [new IRolePlayAdaptiveStateService.InferredSemanticSignal(eventId, 0.8m, "Dean", targetCharacterName, "trigger")]);
    }

    [Fact]
    public async Task ApplyInferredSemanticEvidence_PerStatCap_ClampsDeltaToConfiguredCap()
    {
        var options = new StoryAnalysisOptions
        {
            SemanticStatPerTurnCapByStat = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Loyalty"] = 2
            }
        };
        var (service, session) = CreateSemanticCapFixture(options, "Loyalty", 5m, "decrease", 50);

        var updated = await ApplySemanticCapSignalAsync(service, session, "semantic-cap-event");

        // Raw -5 but capped at 2 → 50 - 2 = 48.
        Assert.Equal(48, updated.CharacterStats["Becky"].Loyalty);
        var breakdown = updated.SemanticStatDeltaBreakdowns.Single(x => x.StatName == "Loyalty");
        Assert.Equal(-5m, breakdown.RawDelta);
        Assert.Equal(-2m, breakdown.AppliedDelta);
        Assert.Equal(-3m, breakdown.CappedDelta);
        Assert.Equal(DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.SemanticCappedPerTurn, breakdown.SuppressionReasonCode);
    }

    [Fact]
    public async Task ApplyInferredSemanticEvidence_PerStatCap_AggregatesAcrossMultipleSignals()
    {
        // Two events both hit Loyalty in the same interaction: -3 and -4 → raw -7, capped to -2 total.
        var options = new StoryAnalysisOptions
        {
            SemanticStatPerTurnCapByStat = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Loyalty"] = 2
            }
        };
        var rpThemeService = new SemanticRpThemeService(
            new Dictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticEventMapping>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticStatMapping>>(StringComparer.OrdinalIgnoreCase)
            {
                ["ev-a"] = [new DreamGenClone.Domain.RolePlay.RPSemanticStatMapping { EventId = "ev-a", ThemeId = "theme-corruption", TargetStat = "Loyalty", Delta = 3m, Direction = "decrease", ConfidenceMin = 0m, ConfidenceMax = 1m, ReasonCode = "a" }],
                ["ev-b"] = [new DreamGenClone.Domain.RolePlay.RPSemanticStatMapping { EventId = "ev-b", ThemeId = "theme-corruption", TargetStat = "Loyalty", Delta = 4m, Direction = "decrease", ConfidenceMin = 0m, ConfidenceMax = 1m, ReasonCode = "b" }]
            });

        var service = new RolePlayAdaptiveStateService(
            new SemanticThemeCatalogService(),
            new EmptyThemePreferenceService(),
            rpThemeService,
            statKeywordCategoryService: null,
            new NullSteeringProfileService(),
            new RecordingDebugSink(),
            NullLogger<RolePlayAdaptiveStateService>.Instance,
            intensityProfileService: null,
            storyAnalysisOptions: Options.Create(options));

        var session = new RolePlaySession
        {
            Id = Guid.NewGuid().ToString(),
            PersonaName = "Ken",
            SelectedRPThemeProfileId = "profile-semantic",
            AdaptiveState = new AdaptiveScenarioState
            {
                ThemeScores = new Dictionary<string, ThemeScoreState>(StringComparer.OrdinalIgnoreCase)
                {
                    ["theme-corruption"] = new ThemeScoreState { ThemeId = "theme-corruption", ThemeName = "Corruption", Score = 10, Breakdown = new ThemeScoreBreakdownV2() }
                },
                CharacterRoles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Becky"] = "Wife", ["Dean"] = "OtherMan" }
            }
        };
        _ = session.AdaptiveState.CharacterStats;
        session.AdaptiveState.CharacterStats["Becky"] = new CharacterStatProfileV2 { CharacterId = "Becky", CharacterRole = "Wife", Loyalty = 50 };

        var interaction = new RolePlayInteraction { Id = $"cap-{Guid.NewGuid():N}", ActorName = "Dean", Content = "trigger" };
        var updated = await service.ApplyInferredSemanticEvidenceAsync(
            session, interaction,
            [
                new IRolePlayAdaptiveStateService.InferredSemanticSignal("ev-a", 0.8m, "Dean", "Becky", "a"),
                new IRolePlayAdaptiveStateService.InferredSemanticSignal("ev-b", 0.8m, "Dean", "Becky", "b")
            ]);

        // Both events target Loyalty; per-turn cap of 2 wins: 50 - 2 = 48.
        Assert.Equal(48, updated.CharacterStats["Becky"].Loyalty);
        var breakdowns = updated.SemanticStatDeltaBreakdowns.Where(x => x.StatName == "Loyalty").ToList();
        Assert.Equal(2, breakdowns.Count);
        Assert.True(breakdowns.All(x => x.SuppressionReasonCode == DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.SemanticCappedPerTurn));
        // First event consumes the full cap budget; second is fully capped.
    }

    [Fact]
    public async Task ApplyInferredSemanticEvidence_PerStatCap_NoCapWhenStatNotConfigured()
    {
        var options = new StoryAnalysisOptions
        {
            SemanticStatPerTurnCapByStat = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Desire"] = 2   // Loyalty intentionally not capped
            }
        };
        var (service, session) = CreateSemanticCapFixture(options, "Loyalty", 5m, "decrease", 50);

        var updated = await ApplySemanticCapSignalAsync(service, session, "semantic-cap-event");

        Assert.Equal(45, updated.CharacterStats["Becky"].Loyalty); // no cap → full -5
        var breakdown = updated.SemanticStatDeltaBreakdowns.Single(x => x.StatName == "Loyalty");
        Assert.Equal(0m, breakdown.CappedDelta);
        Assert.Null(breakdown.SuppressionReasonCode);
    }

    [Fact]
    public async Task ApplyInferredSemanticEvidence_FinalBand_DampsRisingStatNear100()
    {
        var options = new StoryAnalysisOptions
        {
            SemanticStatFinalBandHighStart = 70
        };
        var (service, session) = CreateSemanticCapFixture(options, "Desire", 5m, "increase", 90);

        var updated = await ApplySemanticCapSignalAsync(service, session, "semantic-cap-event");

        // scale = (100 - 90) / (100 - 70) = 0.333 → 5 * 0.333 = 1.67 → floor → +1
        Assert.Equal(91, updated.CharacterStats["Becky"].Desire);
    }

    [Fact]
    public async Task ApplyInferredSemanticEvidence_FinalBand_DampsFallingStatNear0()
    {
        var options = new StoryAnalysisOptions
        {
            SemanticStatFinalBandLowStart = 30
        };
        var (service, session) = CreateSemanticCapFixture(options, "Loyalty", 5m, "decrease", 10);

        var updated = await ApplySemanticCapSignalAsync(service, session, "semantic-cap-event");

        // scale = 10 / 30 = 0.333 → -5 * 0.333 = -1.67 → ceil → -1 (toward zero) → 10 - 1 = 9
        Assert.Equal(9, updated.CharacterStats["Becky"].Loyalty);
    }

    [Fact]
    public async Task ApplyInferredSemanticEvidence_FinalBand_NoDampingOutsideBand()
    {
        var options = new StoryAnalysisOptions
        {
            SemanticStatFinalBandHighStart = 70,
            SemanticStatFinalBandLowStart = 30
        };
        var (service, session) = CreateSemanticCapFixture(options, "Desire", 5m, "increase", 50);

        var updated = await ApplySemanticCapSignalAsync(service, session, "semantic-cap-event");

        // 50 is outside the high band (70+) → full +5.
        Assert.Equal(55, updated.CharacterStats["Becky"].Desire);
    }

    // ── Behavioral dimension per-turn cap + final band ──────────────────────

    private static (RolePlayAdaptiveStateService Service, RolePlaySession Session) CreateDimensionCapFixture(
        StoryAnalysisOptions options,
        IReadOnlyDictionary<string, decimal> eventDeltas,
        string? seedDimension = null,
        int seedDimensionValue = 50)
    {
        var statMappings = new Dictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticStatMapping>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (evId, delta) in eventDeltas)
        {
            // Each event decreases the given stat to keep the drift direction deterministic.
            statMappings[evId] =
            [
                new DreamGenClone.Domain.RolePlay.RPSemanticStatMapping
                {
                    EventId = evId,
                    ThemeId = "theme-corruption",
                    TargetStat = "Restraint",
                    Delta = delta,
                    Direction = "decrease",
                    ConfidenceMin = 0m,
                    ConfidenceMax = 1m,
                    ReasonCode = $"dim-test-{evId}"
                }
            ];
        }

        var rpThemeService = new SemanticRpThemeService(
            new Dictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticEventMapping>>(StringComparer.OrdinalIgnoreCase),
            statMappings);

        var service = new RolePlayAdaptiveStateService(
            new SemanticThemeCatalogService(),
            new EmptyThemePreferenceService(),
            rpThemeService,
            statKeywordCategoryService: null,
            new NullSteeringProfileService(),
            new RecordingDebugSink(),
            NullLogger<RolePlayAdaptiveStateService>.Instance,
            intensityProfileService: null,
            storyAnalysisOptions: Options.Create(options));

        var session = new RolePlaySession
        {
            Id = Guid.NewGuid().ToString(),
            PersonaName = "Ken",
            SelectedRPThemeProfileId = "profile-semantic",
            AdaptiveState = new AdaptiveScenarioState
            {
                ThemeScores = new Dictionary<string, ThemeScoreState>(StringComparer.OrdinalIgnoreCase)
                {
                    ["theme-corruption"] = new ThemeScoreState
                    {
                        ThemeId = "theme-corruption",
                        ThemeName = "Corruption",
                        Score = 10,
                        Breakdown = new ThemeScoreBreakdownV2()
                    }
                },
                CharacterRoles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Becky"] = "Wife",
                    ["Dean"] = "OtherMan"
                }
            }
        };

        _ = session.AdaptiveState.CharacterStats;
        var wife = new CharacterStatProfileV2
        {
            CharacterId = "Becky",
            CharacterRole = "Wife",
            Desire = 50,
            Restraint = 50,
            Loyalty = 50,
            SelfRespect = 50,
            Dominance = 50,
            RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["DiscoveryCaution"] = 50,
                ["Exhibitionism"] = 50,
                ["EmotionalEngagement"] = 50,
                ["PostEncounterGuilt"] = 50,
                ["BoundaryFirmness"] = 50,
                ["SeductionReceptivity"] = 50
            }
        };
        if (seedDimension is not null)
        {
            wife.RuntimeEncounterStats[seedDimension] = seedDimensionValue;
        }
        session.AdaptiveState.CharacterStats["Becky"] = wife;

        return (service, session);
    }

    [Fact]
    public async Task ApplyInferredSemanticEvidence_DimensionCap_ClampsDriftFromMultipleStats()
    {
        // BoundaryFirmness is fed by Restraint (+0.90). Two events in one interaction each move
        // Restraint by -2 → drift on BoundaryFirmness would be round(0.9*-2) + round(0.9*-2) = -4
        // without a dimension cap. With the dimension cap at 2, the total drift must clamp to -2.
        var options = new StoryAnalysisOptions
        {
            SemanticBehavioralDimensionPerTurnCapByDimension = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["BoundaryFirmness"] = 2
            }
        };
        var (service, session) = CreateDimensionCapFixture(
            options,
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["ev-restraint"] = 2m,
                ["ev-loyalty"] = 2m
            });

        var interaction = new RolePlayInteraction { Id = $"dim-{Guid.NewGuid():N}", ActorName = "Dean", Content = "trigger" };
        var updated = await service.ApplyInferredSemanticEvidenceAsync(
            session, interaction,
            [
                new IRolePlayAdaptiveStateService.InferredSemanticSignal("ev-restraint", 0.8m, "Dean", "Becky", "r"),
                new IRolePlayAdaptiveStateService.InferredSemanticSignal("ev-loyalty", 0.8m, "Dean", "Becky", "l")
            ]);

        // Both events drive Restraint down (BoundaryFirmness +0.90) — but the dimension cap of 2
        // wins: only one -2 can land. Net BoundaryFirmness = 50 - 2 = 48, not 46.
        Assert.Equal(48, updated.CharacterStats["Becky"].RuntimeEncounterStats["BoundaryFirmness"]);
    }

    [Fact]
    public async Task ApplyInferredSemanticEvidence_DimensionCap_NoCapWhenDimensionNotConfigured()
    {
        var options = new StoryAnalysisOptions
        {
            SemanticBehavioralDimensionPerTurnCapByDimension = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Exhibitionism"] = 2   // BoundaryFirmness intentionally not capped
            }
        };
        var (service, session) = CreateDimensionCapFixture(
            options,
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["ev-restraint"] = 2m,
                ["ev-loyalty"] = 2m
            });

        var interaction = new RolePlayInteraction { Id = $"dim-{Guid.NewGuid():N}", ActorName = "Dean", Content = "trigger" };
        var updated = await service.ApplyInferredSemanticEvidenceAsync(
            session, interaction,
            [
                new IRolePlayAdaptiveStateService.InferredSemanticSignal("ev-restraint", 0.8m, "Dean", "Becky", "r"),
                new IRolePlayAdaptiveStateService.InferredSemanticSignal("ev-loyalty", 0.8m, "Dean", "Becky", "l")
            ]);

        // BoundaryFirmness uncapped: both -2 drifts land → 50 - 2 - 2 = 46.
        Assert.Equal(46, updated.CharacterStats["Becky"].RuntimeEncounterStats["BoundaryFirmness"]);
    }

    [Fact]
    public async Task ApplyInferredSemanticEvidence_DimensionFinalBand_DampsRisingDimensionNear100()
    {
        // Restraint -2 → Exhibitionism drift = round(-0.60 * -2) = +1 (rising). Seed Exhibitionism
        // at 90, high band start 70 → scale = (100-90)/(100-70) = 0.333 → 1 * 0.333 = 0.33 → round = 0.
        var options = new StoryAnalysisOptions
        {
            SemanticStatFinalBandHighStart = 70,
            SemanticStatFinalBandLowStart = 30
        };
        var (service, session) = CreateDimensionCapFixture(
            options,
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["ev-restraint"] = 2m },
            seedDimension: "Exhibitionism",
            seedDimensionValue: 90);

        var updated = await ApplySemanticCapSignalAsync(service, session, "ev-restraint");

        // Damped to near-stop: 90 + 0 = 90 (not 91).
        Assert.Equal(90, updated.CharacterStats["Becky"].RuntimeEncounterStats["Exhibitionism"]);
    }

    [Fact]
    public async Task ApplyInferredSemanticEvidence_DimensionFinalBand_DampsFallingDimensionNear0()
    {
        // Restraint -2 → PostEncounterGuilt drift = round(0.45 * -2) = -1 (falling). Seed
        // PostEncounterGuilt at 10, low band start 30 → scale = 10/30 = 0.333 → -1 * 0.333 = -0.33 → round = 0.
        var options = new StoryAnalysisOptions
        {
            SemanticStatFinalBandHighStart = 70,
            SemanticStatFinalBandLowStart = 30
        };
        var (service, session) = CreateDimensionCapFixture(
            options,
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["ev-restraint"] = 2m },
            seedDimension: "PostEncounterGuilt",
            seedDimensionValue: 10);

        var updated = await ApplySemanticCapSignalAsync(service, session, "ev-restraint");

        // Damped to near-stop: 10 + 0 = 10 (not 9).
        Assert.Equal(10, updated.CharacterStats["Becky"].RuntimeEncounterStats["PostEncounterGuilt"]);
    }

    [Fact]
    public async Task ApplyInferredSemanticEvidence_DimensionFinalBand_NoDampingOutsideBand()
    {
        var options = new StoryAnalysisOptions
        {
            SemanticStatFinalBandHighStart = 70,
            SemanticStatFinalBandLowStart = 30
        };
        var (service, session) = CreateDimensionCapFixture(
            options,
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["ev-restraint"] = 2m },
            seedDimension: "PostEncounterGuilt",
            seedDimensionValue: 50);

        var updated = await ApplySemanticCapSignalAsync(service, session, "ev-restraint");

        // PostEncounterGuilt at 50 (outside low band) → full -1 → 49.
        Assert.Equal(49, updated.CharacterStats["Becky"].RuntimeEncounterStats["PostEncounterGuilt"]);
    }

    private sealed class FakeIntensityProfileService : IIntensityProfileService
    {
        private readonly List<IntensityProfile> _profiles =
        [
            new() { Id = "atmospheric", Name = "Atmospheric", Intensity = IntensityLevel.Intro },
            new() { Id = "emotional", Name = "Emotional", Intensity = IntensityLevel.Emotional },
            new() { Id = "suggestive", Name = "Suggestive", Intensity = IntensityLevel.SuggestivePg12 },
            new() { Id = "sensual", Name = "Sensual", Intensity = IntensityLevel.SensualMature },
            new() { Id = "explicit", Name = "Explicit", Intensity = IntensityLevel.Explicit },
            new() { Id = "hardcore", Name = "Hardcore", Intensity = IntensityLevel.Hardcore }
        ];

        public Task<IntensityProfile> CreateAsync(
            string name,
            string description,
            IntensityLevel intensity,
            int buildUpPhaseOffset,
            int committedPhaseOffset,
            int approachingPhaseOffset,
            int climaxPhaseOffset,
            int resetPhaseOffset,
            string proseStyleDirective = "",
            string voiceDirective = "",
            string toneDirective = "",
            string focusDirective = "",
            string heatLevelDirective = "",
            CancellationToken cancellationToken = default)
        {
            var created = new IntensityProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Description = description,
                Intensity = intensity,
                BuildUpPhaseOffset = buildUpPhaseOffset,
                CommittedPhaseOffset = committedPhaseOffset,
                ApproachingPhaseOffset = approachingPhaseOffset,
                ClimaxPhaseOffset = climaxPhaseOffset,
                ResetPhaseOffset = resetPhaseOffset
            };

            _profiles.Add(created);
            return Task.FromResult(created);
        }

        public Task<List<IntensityProfile>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_profiles.ToList());

        public Task<IntensityProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_profiles.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)));

        public Task<IntensityProfile?> UpdateAsync(
            string id,
            string name,
            string description,
            IntensityLevel intensity,
            int buildUpPhaseOffset,
            int committedPhaseOffset,
            int approachingPhaseOffset,
            int climaxPhaseOffset,
            int resetPhaseOffset,
            string? proseStyleDirective = null,
            string? voiceDirective = null,
            string? toneDirective = null,
            string? focusDirective = null,
            string? heatLevelDirective = null,
            CancellationToken cancellationToken = default)
        {
            var existing = _profiles.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                return Task.FromResult<IntensityProfile?>(null);
            }

            existing.Name = name;
            existing.Description = description;
            existing.Intensity = intensity;
            existing.BuildUpPhaseOffset = buildUpPhaseOffset;
            existing.CommittedPhaseOffset = committedPhaseOffset;
            existing.ApproachingPhaseOffset = approachingPhaseOffset;
            existing.ClimaxPhaseOffset = climaxPhaseOffset;
            existing.ResetPhaseOffset = resetPhaseOffset;
            existing.ProseStyleDirective = proseStyleDirective ?? existing.ProseStyleDirective;
            existing.VoiceDirective = voiceDirective ?? existing.VoiceDirective;
            existing.ToneDirective = toneDirective ?? existing.ToneDirective;
            existing.FocusDirective = focusDirective ?? existing.FocusDirective;
            existing.HeatLevelDirective = heatLevelDirective ?? existing.HeatLevelDirective;
            existing.UpdatedUtc = DateTime.UtcNow;
            return Task.FromResult<IntensityProfile?>(existing);
        }

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            var removed = _profiles.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(removed > 0);
        }
    }

    private sealed class PolicyThemeCatalogService : IThemeCatalogService
    {
        private static readonly IReadOnlyList<ThemeCatalogEntry> Entries =
        [
            new()
            {
                Id = "theme-a",
                Label = "Theme A",
                Keywords = ["party", "people"],
                Weight = 5,
                StatAffinities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Desire"] = 6
                },
                IsEnabled = true,
                IsBuiltIn = true
            },
            new()
            {
                Id = "theme-b",
                Label = "Theme B",
                Keywords = ["party"],
                Weight = 3,
                StatAffinities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Desire"] = 6
                },
                IsEnabled = true,
                IsBuiltIn = true
            },
            new()
            {
                Id = "theme-c",
                Label = "Theme C",
                Keywords = ["party"],
                Weight = 2,
                StatAffinities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Desire"] = 6
                },
                IsEnabled = true,
                IsBuiltIn = true
            }
        ];

        public Task<ThemeCatalogEntry?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(Entries.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<ThemeCatalogEntry>> GetAllAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
            => Task.FromResult(Entries);

        public Task SaveAsync(ThemeCatalogEntry entry, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SeedDefaultsAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class SemanticThemeCatalogService : IThemeCatalogService
    {
        private static readonly IReadOnlyList<ThemeCatalogEntry> Entries =
        [
            new()
            {
                Id = "theme-corruption",
                Label = "Corruption",
                Keywords = ["placeholder"],
                Weight = 5,
                StatAffinities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Desire"] = 0
                },
                IsEnabled = true,
                IsBuiltIn = true
            }
        ];

        public Task<ThemeCatalogEntry?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(Entries.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<ThemeCatalogEntry>> GetAllAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
            => Task.FromResult(Entries);

        public Task SaveAsync(ThemeCatalogEntry entry, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SeedDefaultsAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class EmptyThemePreferenceService : IThemePreferenceService
    {
        public Task<ThemePreference> CreateAsync(string profileId, string name, string description, ThemeTier tier, string? catalogId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ThemePreference());

        public Task<List<ThemePreference>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<ThemePreference>());

        public Task<List<ThemePreference>> ListByProfileAsync(string profileId, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<ThemePreference>());

        public Task<ThemePreference?> UpdateAsync(string id, string name, string description, ThemeTier tier, string? catalogId = null, CancellationToken cancellationToken = default)
            => Task.FromResult<ThemePreference?>(null);

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<int> AutoLinkToCatalogAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class NullSteeringProfileService : ISteeringProfileService
    {
        public Task<SteeringProfile> CreateAsync(string name, string description, string example, string ruleOfThumb, Dictionary<string, int>? themeAffinities = null, List<string>? escalatingThemeIds = null, Dictionary<string, int>? statBias = null, string immersionDirective = "", string actionDirective = "", int wordTargetMin = 0, int wordTargetMax = 0, int narrativeWordTargetMin = 0, int narrativeWordTargetMax = 0, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<List<SteeringProfile>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<SteeringProfile>());

        public Task<SteeringProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<SteeringProfile?>(null);

        public Task<SteeringProfile?> UpdateAsync(string id, string name, string description, string example, string ruleOfThumb, Dictionary<string, int>? themeAffinities = null, List<string>? escalatingThemeIds = null, Dictionary<string, int>? statBias = null, string immersionDirective = "", string actionDirective = "", int wordTargetMin = 0, int wordTargetMax = 0, int narrativeWordTargetMin = 0, int narrativeWordTargetMax = 0, CancellationToken cancellationToken = default)
            => Task.FromResult<SteeringProfile?>(null);

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class SemanticRpThemeService : IRPThemeService
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticEventMapping>> _mappings;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticStatMapping>> _statMappings;

        public SemanticRpThemeService(
            IReadOnlyDictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticEventMapping>> mappings,
            IReadOnlyDictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticStatMapping>>? statMappings = null)
        {
            _mappings = mappings;
            _statMappings = statMappings ?? new Dictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticStatMapping>>(StringComparer.OrdinalIgnoreCase);
        }

        public Task<IReadOnlyDictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticEventMapping>>> ResolveSemanticEventMappingsByProfileAsync(string profileId, CancellationToken cancellationToken = default)
            => Task.FromResult(_mappings);

        public Task<IReadOnlyDictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticStatMapping>>> ResolveSemanticStatMappingsByProfileAsync(string profileId, CancellationToken cancellationToken = default)
            => Task.FromResult(_statMappings);

        public Task<RPThemeProfile> SaveProfileAsync(RPThemeProfile profile, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RPThemeProfile>> ListProfilesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RPThemeProfile?> GetProfileAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteProfileAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RPTheme> SaveThemeAsync(RPTheme theme, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RPTheme> CloneThemeAsync(string sourceThemeId, string newThemeId, string newThemeLabel, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RPTheme>> ListThemesAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RPTheme>>(Array.Empty<RPTheme>());
        public Task<IReadOnlyList<RPTheme>> ListThemesByProfileAsync(string profileId, bool includeDisabled = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RPTheme>>(Array.Empty<RPTheme>());
        public Task<RPTheme?> GetThemeAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteThemeAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RPThemeMachineDefinition> SaveMachineDefinitionAsync(RPThemeMachineDefinition definition, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RPThemeMachineDefinition>> ListMachineDefinitionsAsync(string themeId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RPThemeMachineDefinition?> GetMachineDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ActivateMachineDefinitionAsync(string themeId, string machineKey, int version, string actorId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<MachineDefinitionValidationResult> ValidateMachineDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task MigrateSessionMachineVersionAsync(string sessionId, string themeId, string machineKey, int targetVersion, string actorId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RPThemeProfileThemeAssignment> SaveProfileAssignmentAsync(RPThemeProfileThemeAssignment assignment, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RPThemeProfileThemeAssignment>> ListProfileAssignmentsAsync(string profileId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteProfileAssignmentAsync(string assignmentId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RPFinishingMoveMatrixRow> SaveFinishingMoveMatrixRowAsync(RPFinishingMoveMatrixRow row, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RPFinishingMoveMatrixRow>> ListFinishingMoveMatrixRowsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteFinishingMoveMatrixRowAsync(string rowId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> ImportFinishingMoveMatrixRowsFromJsonAsync(string json, bool replaceExisting = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RPSteerPositionMatrixRow> SaveSteerPositionMatrixRowAsync(RPSteerPositionMatrixRow row, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RPSteerPositionMatrixRow>> ListSteerPositionMatrixRowsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteSteerPositionMatrixRowAsync(string rowId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> ImportSteerPositionMatrixRowsFromJsonAsync(string json, bool replaceExisting = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RPThemeImportResult>> ImportFromMarkdownAsync(IReadOnlyList<RPThemeImportFile> files, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RPThemeImportResult>> SyncFromMarkdownDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task TruncateRolePlayAndScenarioDataAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RPPosition> SavePositionAsync(RPPosition entry, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RPPosition>> ListPositionsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RPPosition>> ListPositionsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeletePositionAsync(string entryId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RPFinishLocation> SaveFinishLocationAsync(RPFinishLocation entry, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RPFinishLocation>> ListFinishLocationsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteFinishLocationAsync(string entryId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RPFinishFacialType> SaveFinishFacialTypeAsync(RPFinishFacialType entry, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RPFinishFacialType>> ListFinishFacialTypesAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteFinishFacialTypeAsync(string entryId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RPFinishReceptivityLevel> SaveFinishReceptivityLevelAsync(RPFinishReceptivityLevel entry, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RPFinishReceptivityLevel>> ListFinishReceptivityLevelsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteFinishReceptivityLevelAsync(string entryId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RPFinishHisControlLevel> SaveFinishHisControlLevelAsync(RPFinishHisControlLevel entry, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RPFinishHisControlLevel>> ListFinishHisControlLevelsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteFinishHisControlLevelAsync(string entryId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RPFinishTransitionAction> SaveFinishTransitionActionAsync(RPFinishTransitionAction entry, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RPFinishTransitionAction>> ListFinishTransitionActionsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteFinishTransitionActionAsync(string entryId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class RecordingDebugSink : IRolePlayDebugEventSink
    {
        public List<RolePlayDebugEventRecord> Records { get; } = [];

        public Task WriteAsync(RolePlayDebugEventRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }
}