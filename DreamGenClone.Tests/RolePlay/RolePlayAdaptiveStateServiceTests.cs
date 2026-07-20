using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task UpdateFromInteractionAsync_EscalatesAdaptiveIntensity_WhenSignalIsHigh()
    {
        var intensityService = new FakeIntensityProfileService();
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService(), intensityService);
        var session = new RolePlaySession
        {
            SelectedIntensityProfileId = "suggestive",
            AdaptiveIntensityProfileId = "suggestive",
            Interactions =
            [
                new RolePlayInteraction { ActorName = "Seed", Content = "seed-1" },
                new RolePlayInteraction { ActorName = "Seed", Content = "seed-2" },
                new RolePlayInteraction { ActorName = "Seed", Content = "seed-3" },
                new RolePlayInteraction { ActorName = "Seed", Content = "seed-4" }
            ],
            AdaptiveState = new AdaptiveScenarioState
            {
                CharacterSnapshots = [new CharacterStatProfileV2
                    {
                        CharacterId = "becky",
                            Desire = 90,
                            Restraint = 20,
                            Dominance = 50,
                            Loyalty = 50,
                            SelfRespect = 50,
                            RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 30, ["Connection"] = 50 }
                    }]
            }
        };

        await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Becky",
            Content = "I move closer and want to kiss you right now."
        });

        Assert.Equal("sensual", session.AdaptiveIntensityProfileId);
        Assert.Contains("desire-high-restraint-low-escalate", session.AdaptiveIntensityLastTransitionReason);
        Assert.Single(session.AdaptiveIntensityTransitions);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_DeescalatesAdaptiveIntensity_WhenRestraintIsHigh()
    {
        var intensityService = new FakeIntensityProfileService();
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService(), intensityService);
        var session = new RolePlaySession
        {
            SelectedIntensityProfileId = "sensual",
            AdaptiveIntensityProfileId = "sensual",
            AdaptiveState = new AdaptiveScenarioState
            {
                CharacterSnapshots = [new CharacterStatProfileV2
                    {
                        CharacterId = "alex",
                            Desire = 25,
                            Restraint = 90,
                            Dominance = 50,
                            Loyalty = 50,
                            SelfRespect = 50,
                            RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 40, ["Connection"] = 50 }
                    }]
            }
        };

        await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Alex",
            Content = "I hesitate and step back, this feels wrong."
        });

        Assert.Equal("suggestive", session.AdaptiveIntensityProfileId);
        Assert.Contains("desire-low-or-restraint-high-deescalate", session.AdaptiveIntensityLastTransitionReason);
        Assert.Single(session.AdaptiveIntensityTransitions);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_DoesNotTransitionAdaptiveIntensity_WhenManuallyPinned()
    {
        var intensityService = new FakeIntensityProfileService();
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService(), intensityService);
        var session = new RolePlaySession
        {
            IsIntensityManuallyPinned = true,
            SelectedIntensityProfileId = "sensual",
            AdaptiveIntensityProfileId = "suggestive",
            AdaptiveState = new AdaptiveScenarioState
            {
                CharacterSnapshots = [new CharacterStatProfileV2
                    {
                        CharacterId = "alex",
                            Desire = 95,
                            Restraint = 10,
                            Dominance = 50,
                            Loyalty = 50,
                            SelfRespect = 50,
                            RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 20, ["Connection"] = 50 }
                    }]
            }
        };

        await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Alex",
            Content = "I burn with desire and move in."
        });

        Assert.Equal("suggestive", session.AdaptiveIntensityProfileId);
        Assert.Equal("manual-pin-suppressed", session.AdaptiveIntensityLastTransitionReason);
        Assert.Empty(session.AdaptiveIntensityTransitions);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_RespectsCeiling_WhenEscalationWouldExceedBound()
    {
        var intensityService = new FakeIntensityProfileService();
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService(), intensityService);
        var session = new RolePlaySession
        {
            SelectedIntensityProfileId = "suggestive",
            AdaptiveIntensityProfileId = "suggestive",
            IntensityCeilingOverride = "Suggestive",
            Interactions =
            [
                new RolePlayInteraction { ActorName = "Seed", Content = "seed-1" },
                new RolePlayInteraction { ActorName = "Seed", Content = "seed-2" },
                new RolePlayInteraction { ActorName = "Seed", Content = "seed-3" },
                new RolePlayInteraction { ActorName = "Seed", Content = "seed-4" }
            ],
            AdaptiveState = new AdaptiveScenarioState
            {
                CharacterSnapshots = [new CharacterStatProfileV2
                    {
                        CharacterId = "becky",
                            Desire = 90,
                            Restraint = 20,
                            Dominance = 50,
                            Loyalty = 50,
                            SelfRespect = 50,
                            RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 30, ["Connection"] = 50 }
                    }]
            }
        };

        await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Becky",
            Content = "I move closer and want to kiss you right now."
        });

        Assert.Equal("suggestive", session.AdaptiveIntensityProfileId);
        Assert.Contains("blocked-by-ceiling", session.AdaptiveIntensityLastTransitionReason);
        Assert.Empty(session.AdaptiveIntensityTransitions);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_UsesApproachingPhaseFlowBaseline()
    {
        var intensityService = new FakeIntensityProfileService();
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService(), intensityService);
        var session = new RolePlaySession
        {
            SelectedIntensityProfileId = "suggestive",
            AdaptiveIntensityProfileId = "suggestive",
            AdaptiveState = new AdaptiveScenarioState
            {
                CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Approaching,
                CharacterSnapshots = [new CharacterStatProfileV2
                    {
                        CharacterId = "alex",
                            Desire = 60,
                            Restraint = 50,
                            Dominance = 50,
                            Loyalty = 50,
                            SelfRespect = 50,
                            RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 50, ["Connection"] = 50 }
                    }]
            }
        };

        await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Alex",
            Content = "I stay close and keep the tension alive."
        });

        Assert.Equal("sensual", session.AdaptiveIntensityProfileId);
        Assert.Contains("phase=Approaching", session.AdaptiveIntensityLastTransitionReason);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_CapsApproachingPhaseAtErotic()
    {
        var intensityService = new FakeIntensityProfileService();
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService(), intensityService);
        var session = new RolePlaySession
        {
            SelectedIntensityProfileId = "explicit",
            AdaptiveIntensityProfileId = "hardcore",
            Interactions =
            [
                new RolePlayInteraction { ActorName = "Seed", Content = "seed-1" },
                new RolePlayInteraction { ActorName = "Seed", Content = "seed-2" },
                new RolePlayInteraction { ActorName = "Seed", Content = "seed-3" },
                new RolePlayInteraction { ActorName = "Seed", Content = "seed-4" }
            ],
            AdaptiveState = new AdaptiveScenarioState
            {
                CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Approaching,
                CharacterSnapshots = [new CharacterStatProfileV2
                    {
                        CharacterId = "alex",
                            Desire = 90,
                            Restraint = 20,
                            Dominance = 50,
                            Loyalty = 50,
                            SelfRespect = 50,
                            RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 30, ["Connection"] = 50 }
                    }]
            }
        };

        await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Alex",
            Content = "I pull you closer with urgent need."
        });

        Assert.Equal("explicit", session.AdaptiveIntensityProfileId);
        Assert.Contains("approaching-capped-at-erotic", session.AdaptiveIntensityLastTransitionReason);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_UsesClimaxPhaseFlowBaseline()
    {
        var intensityService = new FakeIntensityProfileService();
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService(), intensityService);
        var session = new RolePlaySession
        {
            SelectedIntensityProfileId = "suggestive",
            AdaptiveIntensityProfileId = "suggestive",
            AdaptiveState = new AdaptiveScenarioState
            {
                CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Climax,
                CharacterSnapshots = [new CharacterStatProfileV2
                    {
                        CharacterId = "alex",
                            Desire = 62,
                            Restraint = 48,
                            Dominance = 50,
                            Loyalty = 50,
                            SelfRespect = 50,
                            RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 62, ["Connection"] = 50 }
                    }]
            }
        };

        await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Alex",
            Content = "The climax arrives with full intensity."
        });

        Assert.Equal("explicit", session.AdaptiveIntensityProfileId);
        Assert.Contains("phase=Climax", session.AdaptiveIntensityLastTransitionReason);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_InitializesCharacterStatsAndThemes()
    {
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService());
        var session = new RolePlaySession { PersonaName = "Ken" };
        var interaction = new RolePlayInteraction
        {
            ActorName = "Becky",
            Content = "I watch from the shadows and feel a dangerous thrill and desire."
        };

        var state = await service.UpdateFromInteractionAsync(session, interaction);

        Assert.True(state.CharacterStats.ContainsKey("Becky"));
        Assert.NotNull(state.CharacterStats["Becky"]);
        Assert.Equal(10, state.ThemeScores.Count);
        Assert.False(string.IsNullOrWhiteSpace(state.PrimaryThemeId));
        Assert.False(string.IsNullOrWhiteSpace(state.SecondaryThemeId));
        Assert.Equal("Top2Blend", state.ThemeSelectionRule);
        Assert.NotEmpty(state.RecentEvidence);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_ClampsStatValues()
    {
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService());
        var session = new RolePlaySession();

        // High repetition should not push deltas beyond clamp rules.
        var interaction = new RolePlayInteraction
        {
            ActorName = "Dean",
            Content = string.Join(' ', Enumerable.Repeat("control command claim obey desire heat thrill risk", 30))
        };

        var state = await service.UpdateFromInteractionAsync(session, interaction);
        var stats = CharacterStatProfileV2Accessor.GetAllStats(state.CharacterStats["Dean"]);

        Assert.All(stats.Values, value => Assert.InRange(value, 0, 100));
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
    public async Task UpdateFromInteractionAsync_UpdatesLoyaltyAndSelfRespectSignals()
    {
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService());
        var session = new RolePlaySession();

        var increaseState = await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Becky",
            Content = "She keeps her promise and vow, stays faithful and devoted to her husband and commitment, and holds firm boundaries with dignity and respect."
        });

        var increasedStats = increaseState.CharacterStats["Becky"];
        var loyaltyAfterIncrease = increasedStats.Loyalty;
        var selfRespectAfterIncrease = increasedStats.SelfRespect;
        Assert.True(
            loyaltyAfterIncrease > AdaptiveStatCatalog.DefaultValue,
            $"Expected Loyalty > {AdaptiveStatCatalog.DefaultValue}, actual={loyaltyAfterIncrease}");
        Assert.True(
            selfRespectAfterIncrease > AdaptiveStatCatalog.DefaultValue,
            $"Expected SelfRespect > {AdaptiveStatCatalog.DefaultValue}, actual={selfRespectAfterIncrease}");

        var decreaseState = await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Becky",
            Content = "She starts an affair, cheats, betrays trust, keeps it secret, sneaks away with a stranger, and feels humiliated, ashamed, degraded, demeaned, and used."
        });

        var decreasedStats = decreaseState.CharacterStats["Becky"];
        Assert.True(decreasedStats.Loyalty < loyaltyAfterIncrease);
        Assert.True(decreasedStats.SelfRespect < selfRespectAfterIncrease);
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

    [Fact]
    public async Task UpdateFromInteractionAsync_ProgressesCorruptionFromSemanticIntent_WithoutKeywordTriggers()
    {
        var rpThemeService = new SemanticRpThemeService(
            new Dictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticEventMapping>>(StringComparer.OrdinalIgnoreCase)
            {
                ["lie_to_husband"] =
                [
                    new DreamGenClone.Domain.RolePlay.RPSemanticEventMapping
                    {
                        EventId = "lie_to_husband",
                        ThemeId = "theme-corruption",
                        Delta = 4m,
                        ConfidenceMin = 0m,
                        ConfidenceMax = 1m,
                        Direction = "increase",
                        ReasonCode = "semantic-lie-to-husband"
                    }
                ]
            },
            new Dictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticStatMapping>>(StringComparer.OrdinalIgnoreCase)
            {
                ["lie_to_husband"] =
                [
                    new DreamGenClone.Domain.RolePlay.RPSemanticStatMapping
                    {
                        EventId = "lie_to_husband",
                        ThemeId = "theme-corruption",
                        TargetStat = "Desire",
                        Delta = 3m,
                        ConfidenceMin = 0m,
                        ConfidenceMax = 1m,
                        ReasonCode = "semantic-lie-to-husband-stat"
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
                            Score = 1,
                            Breakdown = new ThemeScoreBreakdownV2()
                        }
                    }
            }
        };

        var interaction = new RolePlayInteraction
        {
            Id = "semantic-lie",
            ActorName = "Becky",
            Content = "Keep this between us. [[semantic:lie_to_husband:0.92]]"
        };
        session.Interactions.Add(interaction);

        var updated = await service.UpdateFromInteractionAsync(session, interaction);

        Assert.True(updated.ThemeScores["theme-corruption"].Score > 1);
        var beckyStats = updated.CharacterStats["Becky"];
        Assert.True(beckyStats.Desire > AdaptiveStatCatalog.DefaultValue);
        var semanticEvent = Assert.Single(updated.SemanticEvents);
        Assert.Equal("lie_to_husband", semanticEvent.EventId);
        Assert.Equal(0.92m, semanticEvent.Confidence);
        Assert.Single(updated.SemanticStatDeltaBreakdowns);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_FailsFast_WhenSemanticStatMappingConfidenceRangeDoesNotMatch()
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
                        Delta = 2m,
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
                        Delta = 3m,
                        ConfidenceMin = 0.95m,
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
                            Score = 5,
                            Breakdown = new ThemeScoreBreakdownV2()
                        }
                    }
            }
        };

        var interaction = new RolePlayInteraction
        {
            Id = "semantic-stat-confidence-mismatch",
            ActorName = "Becky",
            Content = "[[semantic:betrayal:0.7]]"
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateFromInteractionAsync(session, interaction));

        Assert.Contains(DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.ConfidenceOutOfRange, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(session.AdaptiveState.SemanticStepSucceeded);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_CapsSemanticStatDelta_PerTurn()
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
                        Delta = 2m,
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
                        Delta = 12m,
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
                            Score = 5,
                            Breakdown = new ThemeScoreBreakdownV2()
                        }
                    }
            }
        };

        var interaction = new RolePlayInteraction
        {
            Id = "semantic-stat-capped",
            ActorName = "Becky",
            Content = "[[semantic:betrayal:0.9]]"
        };

        var updated = await service.UpdateFromInteractionAsync(session, interaction);

        var statBreakdown = Assert.Single(updated.SemanticStatDeltaBreakdowns);
        Assert.Equal(DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.SemanticCappedPerTurn, statBreakdown.SuppressionReasonCode);
        Assert.True(Math.Abs(statBreakdown.AppliedDelta) <= 1.5m);
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