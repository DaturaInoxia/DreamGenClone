using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Web.Application.Models;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Prompts;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Domain.Models;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayContinuationNarrativeValidationTests
{
    [Fact]
    public async Task ContinueBatchAsync_NarrativePrompt_UsesSceneTransitionGuardrails()
    {
        var completion = new QueueCompletionClient([
            "The crowd drifted toward the terrace while the hallway settled into a quieter rhythm."
        ]);

        var service = CreateService(completion, out _);
        var session = new RolePlaySession
        {
            Id = "s1",
            PersonaName = "Becky"
        };

        var result = await service.ContinueBatchAsync(
            session,
            actors: [],
            includeNarrative: true,
            customActorName: null,
            promptText: "Continue the scene");

        Assert.True(result.Success);
        Assert.Single(completion.Prompts);

        var prompt = completion.Prompts[0];
        Assert.Contains("Your priority is the physical scene and environment", prompt, StringComparison.Ordinal);
        Assert.Contains("Include zero quoted speech", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Prefer externally observable actions, dialogue", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContinueBatchAsync_NarrativeValidation_RetriesAndPrefersSaferOutput()
    {
        var completion = new QueueCompletionClient([
            "\"He doesn't suspect a thing, does he?\" Dean whispered. \"He could hear us,\" Becky said. \"Let him,\" Dean replied.",
            "The hall dimmed as voices from the party receded behind the half-closed door, and the scene shifted toward a tighter, risk-laced stillness."
        ]);

        var service = CreateService(completion, out var debugSink);
        var session = new RolePlaySession
        {
            Id = "s2",
            PersonaName = "Becky"
        };

        var result = await service.ContinueBatchAsync(
            session,
            actors: [],
            includeNarrative: true,
            customActorName: null,
            promptText: "Continue the scene");

        Assert.NotNull(result.NarrativeOutput);
        Assert.Equal("The hall dimmed as voices from the party receded behind the half-closed door, and the scene shifted toward a tighter, risk-laced stillness.", result.NarrativeOutput!.Content);
        Assert.Equal(2, completion.Prompts.Count);

        var validationEvents = debugSink.Records.Where(x => string.Equals(x.EventKind, "NarrativeValidation", StringComparison.Ordinal)).ToList();
        Assert.True(validationEvents.Count >= 2);
        Assert.Contains(validationEvents, x => string.Equals(x.Severity, "Warning", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ContinueBatchAsync_NarrativeValidation_CompliantOutputSkipsRetry()
    {
        var completion = new QueueCompletionClient([
            "Rain tapped softly on the windows while the room settled into a fragile quiet, and the evening eased into its next turn without spectacle."
        ]);

        var service = CreateService(completion, out _);
        var session = new RolePlaySession
        {
            Id = "s3",
            PersonaName = "Becky"
        };

        var result = await service.ContinueBatchAsync(
            session,
            actors: [],
            includeNarrative: true,
            customActorName: null,
            promptText: "Continue the scene");

        Assert.NotNull(result.NarrativeOutput);
        Assert.Equal(1, completion.Prompts.Count);
        Assert.Equal("Rain tapped softly on the windows while the room settled into a fragile quiet, and the evening eased into its next turn without spectacle.", result.NarrativeOutput!.Content);
    }

    [Fact]
    public async Task ContinueAsync_WhenThemeGuidanceEnabled_AppendsThemeHintsToPrompt()
    {
        var completion = new QueueCompletionClient([
            "Dean stepped closer and lowered his voice."
        ]);

        var rpThemeService = new StubRpThemeService(new RPTheme
        {
            Id = "infidelity-public-facade",
            AIGenerationNotes =
            [
                new RPThemeAIGuidanceNote
                {
                    Section = RPThemeAIGuidanceSection.InteractionDynamics,
                    Text = "Escalate excuse complexity over time.",
                    SortOrder = 0
                }
            ]
        });

        var service = CreateService(completion, out _, rpThemeService);
        var session = new RolePlaySession
        {
            Id = "s4",
            PersonaName = "Becky",
            
            UseThemeAIGuidanceNotesInPrompt = true,
            ThemeAIGuidanceInfluencePercent = 55,
            MaxThemeAIGuidanceNotes = 4,
            AdaptiveState = new AdaptiveScenarioState
            {
                ActiveScenarioId = "infidelity-public-facade",
                CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Committed
            }
        };

        await service.ContinueAsync(
            session,
            ContinueAsActor.Npc,
            customActorName: null,
            intent: PromptIntent.Message,
            promptText: "Continue naturally.");

        Assert.Single(completion.Prompts);
        var prompt = completion.Prompts[0];
        Assert.Contains("Theme AI Guidance (soft hints, influence=55%):", prompt, StringComparison.Ordinal);
        Assert.Contains("Escalate excuse complexity over time.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContinueAsync_WhenThemeHasHardConstraints_AppendsAuthoritativeConstraintLines()
    {
        var completion = new QueueCompletionClient([
            "Dean paused at the doorway and watched her expression."
        ]);

        var rpThemeService = new StubRpThemeService(new RPTheme
        {
            Id = "denial-edging",
            AIGenerationNotes =
            [
                new RPThemeAIGuidanceNote
                {
                    Section = RPThemeAIGuidanceSection.HardConstraint,
                    Text = "Do not resolve the restraint arc in this response.",
                    SortOrder = 0
                },
                new RPThemeAIGuidanceNote
                {
                    Section = RPThemeAIGuidanceSection.InteractionDynamics,
                    Text = "Sustain tension with interrupted momentum.",
                    SortOrder = 1
                }
            ]
        });

        var service = CreateService(completion, out _, rpThemeService);
        var session = new RolePlaySession
        {
            Id = "s4-hard",
            PersonaName = "Becky",
            UseThemeAIGuidanceNotesInPrompt = true,
            ThemeAIGuidanceInfluencePercent = 55,
            MaxThemeAIGuidanceNotes = 4,
            AdaptiveState = new AdaptiveScenarioState
            {
                ActiveScenarioId = "denial-edging",
                CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Committed
            }
        };

        await service.ContinueAsync(
            session,
            ContinueAsActor.Npc,
            customActorName: null,
            intent: PromptIntent.Message,
            promptText: "Continue naturally.");

        Assert.Single(completion.Prompts);
        var prompt = completion.Prompts[0];
        Assert.Contains("Theme Hard Constraints (authoritative):", prompt, StringComparison.Ordinal);
        Assert.Contains("HARD CONSTRAINT: Do not resolve the restraint arc in this response.", prompt, StringComparison.Ordinal);
        Assert.Contains("HARD CONSTRAINT — enforce in this response: Do not resolve the restraint arc in this response.", prompt, StringComparison.Ordinal);
        Assert.Contains("Theme AI Guidance (soft hints, influence=55%):", prompt, StringComparison.Ordinal);
        Assert.Contains("Sustain tension with interrupted momentum.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContinueAsync_WhenTop2Blend_AppendsSecondaryThemeHintsAtReducedInfluence()
    {
        var completion = new QueueCompletionClient([
            "Dean stepped closer and lowered his voice."
        ]);

        var primaryTheme = new RPTheme
        {
            Id = "infidelity-public-facade",
            AIGenerationNotes =
            [
                new RPThemeAIGuidanceNote
                {
                    Section = RPThemeAIGuidanceSection.InteractionDynamics,
                    Text = "Primary guidance note.",
                    SortOrder = 0
                }
            ]
        };

        var secondaryTheme = new RPTheme
        {
            Id = "seduction",
            AIGenerationNotes =
            [
                new RPThemeAIGuidanceNote
                {
                    Section = RPThemeAIGuidanceSection.InteractionDynamics,
                    Text = "Secondary guidance note.",
                    SortOrder = 0
                }
            ]
        };

        var rpThemeService = new StubRpThemeService(primaryTheme, secondaryTheme);

        var service = CreateService(completion, out _, rpThemeService);
        var session = new RolePlaySession
        {
            Id = "s4b",
            PersonaName = "Becky",
            UseThemeAIGuidanceNotesInPrompt = true,
            ThemeAIGuidanceInfluencePercent = 55,
            MaxThemeAIGuidanceNotes = 4,
            AdaptiveState = new AdaptiveScenarioState
            {
                ActiveScenarioId = "infidelity-public-facade",
                CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Committed,
                ThemeSelectionRule = "Top2Blend",
                PrimaryThemeId = "infidelity-public-facade",
                SecondaryThemeId = "seduction"
            }
        };

        await service.ContinueAsync(
            session,
            ContinueAsActor.Npc,
            customActorName: null,
            intent: PromptIntent.Message,
            promptText: "Continue naturally.");

        Assert.Single(completion.Prompts);
        var prompt = completion.Prompts[0];
        Assert.Contains("Theme AI Guidance (strong guidance, influence=55%):", prompt, StringComparison.Ordinal);
        Assert.Contains("Theme AI Guidance (soft hints, influence=27%):", prompt, StringComparison.Ordinal);
        Assert.Contains("Primary guidance note.", prompt, StringComparison.Ordinal);
        Assert.Contains("Secondary guidance note.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContinueAsync_WhenTop2BlendMissingSecondaryTheme_DoesNotAppendSecondaryHints()
    {
        var completion = new QueueCompletionClient([
            "Dean stepped closer and lowered his voice."
        ]);

        var rpThemeService = new StubRpThemeService(new RPTheme
        {
            Id = "infidelity-public-facade",
            AIGenerationNotes =
            [
                new RPThemeAIGuidanceNote
                {
                    Section = RPThemeAIGuidanceSection.InteractionDynamics,
                    Text = "Primary guidance note.",
                    SortOrder = 0
                }
            ]
        });

        var service = CreateService(completion, out _, rpThemeService);
        var session = new RolePlaySession
        {
            Id = "s4c",
            PersonaName = "Becky",
            UseThemeAIGuidanceNotesInPrompt = true,
            ThemeAIGuidanceInfluencePercent = 55,
            MaxThemeAIGuidanceNotes = 4,
            AdaptiveState = new AdaptiveScenarioState
            {
                ActiveScenarioId = "infidelity-public-facade",
                CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Committed,
                ThemeSelectionRule = "Top2Blend",
                PrimaryThemeId = "infidelity-public-facade",
                SecondaryThemeId = "missing-theme"
            }
        };

        await service.ContinueAsync(
            session,
            ContinueAsActor.Npc,
            customActorName: null,
            intent: PromptIntent.Message,
            promptText: "Continue naturally.");

        Assert.Single(completion.Prompts);
        var prompt = completion.Prompts[0];
        Assert.Contains("Theme AI Guidance (strong guidance, influence=55%):", prompt, StringComparison.Ordinal);
        Assert.Contains("Primary guidance note.", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("influence=27%", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Secondary guidance note.", prompt, StringComparison.Ordinal);
    }

    // ── T003: NarrativeLocationLabel (tested via prompt output) ─────────────

    [Fact]
    public async Task NarrativeLocationLabel_EmDash_StripsSubtitle()
    {
        var completion = new QueueCompletionClient(["Scene text."]);
        var service = CreateService(completion, out _);
        var session = new RolePlaySession
        {
            Id = "loc1",
            PersonaName = "Becky",
            AdaptiveState = new AdaptiveScenarioState
            {
                CurrentSceneLocation = "Hotel Room \u2014 Private Suite"
            }
        };

        await service.ContinueBatchAsync(session, actors: [], includeNarrative: true, customActorName: null, promptText: "Continue");

        var prompt = completion.Prompts[0];
        Assert.Contains("Hotel Room", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Private Suite", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NarrativeLocationLabel_Colon_StripsSubtitle()
    {
        var completion = new QueueCompletionClient(["Scene text."]);
        var service = CreateService(completion, out _);
        var session = new RolePlaySession
        {
            Id = "loc2",
            PersonaName = "Becky",
            AdaptiveState = new AdaptiveScenarioState
            {
                CurrentSceneLocation = "The Library : Special Collection"
            }
        };

        await service.ContinueBatchAsync(session, actors: [], includeNarrative: true, customActorName: null, promptText: "Continue");

        var prompt = completion.Prompts[0];
        Assert.Contains("The Library", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Special Collection", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NarrativeLocationLabel_PlainName_Unchanged()
    {
        var completion = new QueueCompletionClient(["Scene text."]);
        var service = CreateService(completion, out _);
        var session = new RolePlaySession
        {
            Id = "loc3",
            PersonaName = "Becky",
            AdaptiveState = new AdaptiveScenarioState
            {
                CurrentSceneLocation = "The Garden"
            }
        };

        await service.ContinueBatchAsync(session, actors: [], includeNarrative: true, customActorName: null, promptText: "Continue");

        var prompt = completion.Prompts[0];
        Assert.Contains("The Garden", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NarrativeLocationLabel_NullLocation_NoLocationConstraintInPrompt()
    {
        var completion = new QueueCompletionClient(["Scene text."]);
        var service = CreateService(completion, out _);
        var session = new RolePlaySession
        {
            Id = "loc4",
            PersonaName = "Becky",
            AdaptiveState = new AdaptiveScenarioState
            {
                CurrentSceneLocation = null
            }
        };

        await service.ContinueBatchAsync(session, actors: [], includeNarrative: true, customActorName: null, promptText: "Continue");

        var prompt = completion.Prompts[0];
        Assert.DoesNotContain("HARD CONSTRAINT \u2014 Scene Location", prompt, StringComparison.Ordinal);
    }

    // ── T009: Phase 3 — Prompt construction tests ───────────────────────────

    [Fact]
    public async Task NarrativePrompt_NonClimax_ContainsSceneDescriptionCategories()
    {
        var completion = new QueueCompletionClient(["Scene text."]);
        var service = CreateService(completion, out _);
        var session = new RolePlaySession
        {
            Id = "np1",
            PersonaName = "Becky",
            AdaptiveState = new AdaptiveScenarioState
            {
                CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Committed
            }
        };

        await service.ContinueBatchAsync(session, actors: [], includeNarrative: true, customActorName: null, promptText: "Continue");

        var prompt = completion.Prompts[0];
        Assert.Contains("spatial layout", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("where characters are", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Include zero quoted speech", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NarrativePrompt_Climax_ContainsPhysicalDetailCategories()
    {
        var completion = new QueueCompletionClient(["Scene text with explicit content."]);
        var service = CreateService(completion, out _);
        var session = new RolePlaySession
        {
            Id = "np2",
            PersonaName = "Becky",
            AdaptiveState = new AdaptiveScenarioState
            {
                CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Climax
            }
        };

        await service.ContinueBatchAsync(session, actors: [], includeNarrative: true, customActorName: null, promptText: "Continue");

        var prompt = completion.Prompts[0];
        Assert.Contains("physical contact", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("body part positions", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Include zero quoted speech. Do not write any dialogue in this passage.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NarrativePrompt_LocationSubtitleStripped()
    {
        var completion = new QueueCompletionClient(["Scene text."]);
        var service = CreateService(completion, out _);
        var session = new RolePlaySession
        {
            Id = "np3",
            PersonaName = "Becky",
            AdaptiveState = new AdaptiveScenarioState
            {
                CurrentSceneLocation = "Trailer \u2014 Shared Space"
            }
        };

        await service.ContinueBatchAsync(session, actors: [], includeNarrative: true, customActorName: null, promptText: "Continue");

        var prompt = completion.Prompts[0];
        Assert.Contains("Trailer", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Shared Space", prompt, StringComparison.Ordinal);
    }

    // ── T014: Phase 4 — Validation logic tests ──────────────────────────────

    [Fact]
    public async Task NarrativeValidation_FirstPersonInQuote_DoesNotTriggerRetry()
    {
        // "I wasn't ready" is inside quotes — narrator body has no first-person pronoun.
        // Surrounding prose is long enough to keep quoted-text ratio below the 20% threshold.
        var completion = new QueueCompletionClient([
            "The room was quiet and still as the afternoon light shifted through the curtains, casting long shadows across the polished floor. " +
            "\"I wasn't ready,\" she said quietly, turning away from the window toward the far wall."
        ]);

        var service = CreateService(completion, out var debugSink);
        var session = new RolePlaySession { Id = "v1", PersonaName = "Becky" };

        var result = await service.ContinueBatchAsync(session, actors: [], includeNarrative: true, customActorName: null, promptText: "Continue");

        Assert.NotNull(result.NarrativeOutput);
        Assert.Equal(1, completion.Prompts.Count); // no retry
        var validationEvents = debugSink.Records.Where(x => string.Equals(x.EventKind, "NarrativeValidation", StringComparison.Ordinal)).ToList();
        Assert.Single(validationEvents);
        Assert.All(validationEvents, e => Assert.Equal("Info", e.Severity));
    }

    [Fact]
    public async Task NarrativeValidation_FirstPersonInNarratorBody_TriggersRetry()
    {
        var safeOutput = "The hallway stretched ahead, still and quiet.";
        var completion = new QueueCompletionClient([
            "I moved through the hallway with careful steps.",
            safeOutput
        ]);

        var service = CreateService(completion, out var debugSink);
        var session = new RolePlaySession { Id = "v2", PersonaName = "Becky" };

        var result = await service.ContinueBatchAsync(session, actors: [], includeNarrative: true, customActorName: null, promptText: "Continue");

        Assert.True(completion.Prompts.Count >= 2);
        var warnings = debugSink.Records.Where(x => string.Equals(x.EventKind, "NarrativeValidation", StringComparison.Ordinal) && string.Equals(x.Severity, "Warning", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(warnings);
    }

    [Fact]
    public async Task NarrativeValidation_Interiority_TriggersRetry()
    {
        var safeOutput = "The room settled into a tense quiet.";
        var completion = new QueueCompletionClient([
            "She thought about the previous night and wondered what it meant.",
            safeOutput
        ]);

        var service = CreateService(completion, out var debugSink);
        var session = new RolePlaySession { Id = "v3", PersonaName = "Becky" };

        var result = await service.ContinueBatchAsync(session, actors: [], includeNarrative: true, customActorName: null, promptText: "Continue");

        Assert.True(completion.Prompts.Count >= 2);
        var warnings = debugSink.Records.Where(x => string.Equals(x.EventKind, "NarrativeValidation", StringComparison.Ordinal) && string.Equals(x.Severity, "Warning", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(warnings);
    }

    [Fact]
    public async Task NarrativeValidation_ClimaxMode_SingleQuote_TriggersRetry()
    {
        // climaxMode = true → threshold=1, so even one quoted block triggers retry
        var safeOutput = "The encounter continued at its raw, physical pace.";
        var completion = new QueueCompletionClient([
            "The room fell quiet. \"Stay,\" she breathed. The moment held.",
            safeOutput
        ]);

        var service = CreateService(completion, out var debugSink);
        var session = new RolePlaySession
        {
            Id = "v4",
            PersonaName = "Becky",
            AdaptiveState = new AdaptiveScenarioState
            {
                CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Climax
            }
        };

        var result = await service.ContinueBatchAsync(session, actors: [], includeNarrative: true, customActorName: null, promptText: "Continue");

        Assert.True(completion.Prompts.Count >= 2, "Expected retry when single quote appears in Climax mode");
    }

    [Fact]
    public async Task NarrativeValidation_NonClimaxMode_SingleQuote_NoRetry()
    {
        // climaxMode = false → threshold=2, so one quoted block does NOT trigger retry
        var completion = new QueueCompletionClient([
            "The room fell quiet. \"Stay,\" she breathed. The moment held."
        ]);

        var service = CreateService(completion, out _);
        var session = new RolePlaySession
        {
            Id = "v5",
            PersonaName = "Becky",
            AdaptiveState = new AdaptiveScenarioState
            {
                CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Committed
            }
        };

        await service.ContinueBatchAsync(session, actors: [], includeNarrative: true, customActorName: null, promptText: "Continue");

        Assert.Equal(1, completion.Prompts.Count);
    }

    // ── T017: Phase 5 — Correction prompt tests ─────────────────────────────

    [Fact]
    public async Task CorrectionPrompt_QuotedBlockOnly_ContainsQuoteClause_NotFirstPersonClause()
    {
        // 3 quoted blocks → correction includes quoted-block clause but NOT first-person clause
        var safeOutput = "The corridor stretched toward the exit, empty.";
        var completion = new QueueCompletionClient([
            "\"Hello,\" she said. \"How are you?\" he asked. \"Fine,\" she replied.",
            safeOutput
        ]);

        var service = CreateService(completion, out _);
        var session = new RolePlaySession { Id = "cp1", PersonaName = "Becky" };
        await service.ContinueBatchAsync(session, actors: [], includeNarrative: true, customActorName: null, promptText: "Continue");

        Assert.True(completion.Prompts.Count >= 2);
        var correctionPrompt = completion.Prompts[1];
        Assert.Contains("quoted blocks", correctionPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("first-person pronoun", correctionPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorrectionPrompt_FirstPersonOnly_ContainsFirstPersonClause_NotQuoteClause()
    {
        // Only one first-person pronoun in narrator body, no quoted blocks → first-person clause, no quote clause
        var safeOutput = "The hallway stretched ahead.";
        var completion = new QueueCompletionClient([
            "I watched the scene unfold from the doorway.",
            safeOutput
        ]);

        var service = CreateService(completion, out _);
        var session = new RolePlaySession { Id = "cp2", PersonaName = "Becky" };
        await service.ContinueBatchAsync(session, actors: [], includeNarrative: true, customActorName: null, promptText: "Continue");

        Assert.True(completion.Prompts.Count >= 2);
        var correctionPrompt = completion.Prompts[1];
        Assert.Contains("first-person pronoun", correctionPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("quoted blocks", correctionPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CorrectionPrompt_Interiority_ContainsInteriorityClause()
    {
        var safeOutput = "The room was still.";
        var completion = new QueueCompletionClient([
            "She wondered what the evening had meant for their future.",
            safeOutput
        ]);

        var service = CreateService(completion, out _);
        var session = new RolePlaySession { Id = "cp3", PersonaName = "Becky" };
        await service.ContinueBatchAsync(session, actors: [], includeNarrative: true, customActorName: null, promptText: "Continue");

        Assert.True(completion.Prompts.Count >= 2);
        var correctionPrompt = completion.Prompts[1];
        Assert.Contains("inner-thought phrases", correctionPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorrectionPrompt_AlwaysEndsWithPhysicalSceneClause()
    {
        // Any violation type produces a correction prompt that ends with the physical-scene close
        var safeOutput = "The room was quiet.";
        var completion = new QueueCompletionClient([
            "I moved across the floor carefully.",
            safeOutput
        ]);

        var service = CreateService(completion, out _);
        var session = new RolePlaySession { Id = "cp4", PersonaName = "Becky" };
        await service.ContinueBatchAsync(session, actors: [], includeNarrative: true, customActorName: null, promptText: "Continue");

        Assert.True(completion.Prompts.Count >= 2);
        var correctionPrompt = completion.Prompts[1];
        Assert.Contains("Rewrite focusing on physical scene", correctionPrompt, StringComparison.Ordinal);
    }

    // ── T022: Phase 6 — ContinueNarrativeAsync tests ────────────────────────

    [Fact]
    public async Task ContinueNarrativeAsync_ValidOutput_ReturnsInteractionWithNarrativeValidationEvent()
    {
        var completion = new QueueCompletionClient([
            "Rain tapped on the glass as the room settled."
        ]);

        var service = CreateService(completion, out var debugSink);
        var session = new RolePlaySession { Id = "cna1", PersonaName = "Becky" };

        var interaction = await service.ContinueNarrativeAsync(session, "Narrative", "Continue the scene");

        Assert.Equal("Narrative", interaction.ActorName);
        Assert.Equal("Narrative", interaction.GeneratedByCommand);
        Assert.Equal(DreamGenClone.Web.Domain.RolePlay.InteractionType.System, interaction.InteractionType);

        var validationEvents = debugSink.Records
            .Where(x => string.Equals(x.EventKind, "NarrativeValidation", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(validationEvents);
    }

    [Fact]
    public async Task ContinueNarrativeAsync_ViolatingOutput_RetriesAndEmitsWarning()
    {
        var completion = new QueueCompletionClient([
            "\"Hello,\" she said. \"How are you?\" he asked. \"Just fine,\" she replied with a sigh.",
            "The room settled into quiet after the exchange."
        ]);

        var service = CreateService(completion, out var debugSink);
        var session = new RolePlaySession { Id = "cna2", PersonaName = "Becky" };

        await service.ContinueNarrativeAsync(session, "Narrative", "Continue the scene");

        Assert.True(completion.Prompts.Count >= 2);
        var warnings = debugSink.Records
            .Where(x => string.Equals(x.EventKind, "NarrativeValidation", StringComparison.Ordinal)
                     && string.Equals(x.Severity, "Warning", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(warnings);
    }

    private static RolePlayContinuationService CreateService(
        QueueCompletionClient completion,
        out RecordingDebugEventSink debugSink,
        IRPThemeService? rpThemeService = null)
    {
        debugSink = new RecordingDebugEventSink();

        return new RolePlayContinuationService(
            completion,
            new StubModelResolutionService(),
            new StubModelSettingsService(),
            new RolePlayTestFactory.NullScenarioService(),
            new AllowAllPromptDealbreakerService(),
            new EmptyThemePreferenceService(),
            new NullIntensityProfileService(),
            new NullSteeringProfileService(),
            new StubScenarioGuidanceContextFactory(),
            debugSink,
                new RolePlayPromptBuilder([], new PromptBudgetEnforcer(NullLogger<PromptBudgetEnforcer>.Instance), NullLogger<RolePlayPromptBuilder>.Instance),
                new ActorProfileResolver(),
                new StubPhaseRuleOfThumbRepository(),
                NullLogger<RolePlayContinuationService>.Instance,
                diagnosticsService: null,
                rpThemeService: rpThemeService);
    }

    private sealed class QueueCompletionClient : ICompletionClient
    {
        private readonly Queue<string> _responses;

        public QueueCompletionClient(IEnumerable<string> responses)
        {
            _responses = new Queue<string>(responses);
        }

        public List<string> Prompts { get; } = [];

        public Task<string> GenerateAsync(string prompt, ResolvedModel resolved, CancellationToken cancellationToken = default)
        {
            Prompts.Add(prompt);
            if (_responses.Count == 0)
            {
                return Task.FromResult("fallback narrative");
            }

            return Task.FromResult(_responses.Dequeue());
        }

        public Task<string> GenerateAsync(string systemMessage, string userMessage, ResolvedModel resolved, CancellationToken cancellationToken = default)
            => Task.FromResult("unused");


        public async Task<(string Content, string? Reasoning)> GenerateWithReasoningAsync(string prompt, ResolvedModel resolved, CancellationToken cancellationToken = default)
        {
            var content = await GenerateAsync(prompt, resolved, cancellationToken);
            return (content, null);
        }

        public async Task<(string Content, string? Reasoning)> StreamGenerateWithReasoningAsync(string prompt, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
        {
            var content = await StreamGenerateAsync(prompt, resolved, onChunk, cancellationToken);
            return (content, null);
        }

        public async Task<string> StreamGenerateAsync(string prompt, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
        {
            var content = await GenerateAsync(prompt, resolved, cancellationToken);
            await onChunk(content);
            return content;
        }

        public async Task<string> StreamGenerateAsync(string systemMessage, string userMessage, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
        {
            await onChunk("unused");
            return "unused";
        }

        public Task<bool> CheckHealthAsync(string providerBaseUrl, int timeoutSeconds, string? decryptedApiKey, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<(bool Success, string Message)> CheckModelHealthAsync(string providerBaseUrl, string chatCompletionsPath, int timeoutSeconds, string? decryptedApiKey, string modelIdentifier, CancellationToken cancellationToken = default)
            => Task.FromResult((true, "ok"));

    }

    private sealed class StubPhaseRuleOfThumbRepository : IPhaseRuleOfThumbRepository
    {
        public Task<PhaseRuleOfThumbRow?> GetByPhaseAsync(string phase, CancellationToken ct = default)
            => Task.FromResult<PhaseRuleOfThumbRow?>(new PhaseRuleOfThumbRow(
                $"phase-rot-{phase.ToLowerInvariant()}",
                phase,
                $"Rule of Thumb for {phase}"));
    }

    private sealed class StubModelResolutionService : IModelResolutionService
    {
        public Task<ResolvedModel> ResolveAsync(
            AppFunction function,
            string? sessionModelId = null,
            double? sessionTemperature = null,
            double? sessionTopP = null,
            int? sessionMaxTokens = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ResolvedModel(
                ProviderBaseUrl: "http://localhost",
                ChatCompletionsPath: "/v1/chat/completions",
                ProviderTimeoutSeconds: 30,
                ApiKeyEncrypted: null,
                ModelIdentifier: "test-model",
                Temperature: 0.7,
                TopP: 0.9,
                MaxTokens: 400,
                ProviderName: "test-provider",
                IsSessionOverride: false));
        }
    }

    private sealed class StubModelSettingsService : IModelSettingsService
    {
        public ModelSettings GetSettings(string sessionId) => new();

        public void UpdateSettings(string sessionId, ModelSettings settings)
        {
        }

        public void ClearSettings(string sessionId)
        {
        }
    }

    private sealed class AllowAllPromptDealbreakerService : IPromptDealbreakerService
    {
        public Task<PromptDealbreakerResult> ValidateAsync(string text, string profileId, CancellationToken cancellationToken = default)
            => Task.FromResult(new PromptDealbreakerResult { IsAllowed = true });
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

    private sealed class NullIntensityProfileService : IIntensityProfileService
    {
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
            => throw new NotImplementedException();

        public Task<List<IntensityProfile>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<IntensityProfile>());

        public Task<IntensityProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<IntensityProfile?>(null);

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
            => Task.FromResult<IntensityProfile?>(null);

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
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

    private sealed class StubScenarioGuidanceContextFactory : IScenarioGuidanceContextFactory
    {
        public Task<ScenarioGuidanceContext> CreateAsync(ScenarioGuidanceInput input, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ScenarioGuidanceContext(
                Phase: input.CurrentPhase,
                ActiveScenarioId: input.ActiveScenarioId,
                GuidanceText: "Keep pacing coherent.",
                ExcludedScenarioIds: [],
                CharacterBehavioralFrames: new Dictionary<string, string>(),
                CharacterStatStateTexts: new Dictionary<string, string>()));
        }
    }

    private sealed class StubRpThemeService : IRPThemeService
    {
        private readonly Dictionary<string, RPTheme> _themes;

        public StubRpThemeService(params RPTheme[] themes)
        {
            _themes = themes
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        }

        public Task<RPThemeProfile> SaveProfileAsync(RPThemeProfile profile, CancellationToken cancellationToken = default)
            => Task.FromResult(profile);

        public Task<IReadOnlyList<RPThemeProfile>> ListProfilesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RPThemeProfile>>([]);

        public Task<RPThemeProfile?> GetProfileAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<RPThemeProfile?>(null);

        public Task<bool> DeleteProfileAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<RPTheme> SaveThemeAsync(RPTheme theme, CancellationToken cancellationToken = default)
            => Task.FromResult(theme);

        public Task<RPTheme> CloneThemeAsync(string sourceThemeId, string newThemeId, string newThemeLabel, CancellationToken cancellationToken = default)
        {
            var source = _themes.Values.First();
            return Task.FromResult(new RPTheme
            {
                Id = newThemeId,
                Label = newThemeLabel,
                Description = source.Description,
                Category = source.Category,
                Weight = source.Weight,
                IsEnabled = source.IsEnabled
            });
        }

        public Task<IReadOnlyList<RPTheme>> ListThemesAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RPTheme>>(_themes.Values.ToList());

        public Task<IReadOnlyList<RPTheme>> ListThemesByProfileAsync(string profileId, bool includeDisabled = false, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RPTheme>>(_themes.Values.ToList());

        public Task<IReadOnlyDictionary<string, IReadOnlyList<RPSemanticEventMapping>>> ResolveSemanticEventMappingsByProfileAsync(string profileId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<RPSemanticEventMapping>>>(new Dictionary<string, IReadOnlyList<RPSemanticEventMapping>>(StringComparer.OrdinalIgnoreCase));

        public Task<RPTheme?> GetThemeAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_themes.TryGetValue(id, out var theme) ? theme : null);

        public Task<bool> DeleteThemeAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<RPThemeMachineDefinition> SaveMachineDefinitionAsync(RPThemeMachineDefinition definition, CancellationToken cancellationToken = default)
            => Task.FromResult(definition);

        public Task<IReadOnlyList<RPThemeMachineDefinition>> ListMachineDefinitionsAsync(string themeId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RPThemeMachineDefinition>>([]);

        public Task<RPThemeMachineDefinition?> GetMachineDefinitionAsync(string definitionId, CancellationToken cancellationToken = default)
            => Task.FromResult<RPThemeMachineDefinition?>(null);

        public Task ActivateMachineDefinitionAsync(string themeId, string machineKey, int version, string actorId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<MachineDefinitionValidationResult> ValidateMachineDefinitionAsync(string definitionId, CancellationToken cancellationToken = default)
            => Task.FromResult(new MachineDefinitionValidationResult { IsValid = true });

        public Task MigrateSessionMachineVersionAsync(string sessionId, string themeId, string machineKey, int targetVersion, string actorId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<RPThemeProfileThemeAssignment> SaveProfileAssignmentAsync(RPThemeProfileThemeAssignment assignment, CancellationToken cancellationToken = default)
            => Task.FromResult(assignment);

        public Task<IReadOnlyList<RPThemeProfileThemeAssignment>> ListProfileAssignmentsAsync(string profileId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RPThemeProfileThemeAssignment>>([]);

        public Task<bool> DeleteProfileAssignmentAsync(string assignmentId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<RPFinishingMoveMatrixRow> SaveFinishingMoveMatrixRowAsync(RPFinishingMoveMatrixRow row, CancellationToken cancellationToken = default)
            => Task.FromResult(row);

        public Task<IReadOnlyList<RPFinishingMoveMatrixRow>> ListFinishingMoveMatrixRowsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RPFinishingMoveMatrixRow>>([]);

        public Task<bool> DeleteFinishingMoveMatrixRowAsync(string rowId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<int> ImportFinishingMoveMatrixRowsFromJsonAsync(string json, bool replaceExisting = false, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<RPSteerPositionMatrixRow> SaveSteerPositionMatrixRowAsync(RPSteerPositionMatrixRow row, CancellationToken cancellationToken = default)
            => Task.FromResult(row);

        public Task<IReadOnlyList<RPSteerPositionMatrixRow>> ListSteerPositionMatrixRowsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RPSteerPositionMatrixRow>>([]);

        public Task<bool> DeleteSteerPositionMatrixRowAsync(string rowId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<int> ImportSteerPositionMatrixRowsFromJsonAsync(string json, bool replaceExisting = false, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<IReadOnlyList<RPThemeImportResult>> ImportFromMarkdownAsync(IReadOnlyList<RPThemeImportFile> files, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RPThemeImportResult>>([]);

        public Task<IReadOnlyList<RPThemeImportResult>> SyncFromMarkdownDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RPThemeImportResult>>([]);

        public Task TruncateRolePlayAndScenarioDataAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<RPPosition> SavePositionAsync(RPPosition entry, CancellationToken cancellationToken = default) => Task.FromResult(entry);
        public Task<IReadOnlyList<RPPosition>> ListPositionsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RPPosition>>([]);
        public Task<IReadOnlyList<RPPosition>> ListPositionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RPPosition>>([]);
        public Task<bool> DeletePositionAsync(string entryId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<RPFinishLocation> SaveFinishLocationAsync(RPFinishLocation entry, CancellationToken cancellationToken = default) => Task.FromResult(entry);
        public Task<IReadOnlyList<RPFinishLocation>> ListFinishLocationsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RPFinishLocation>>([]);
        public Task<bool> DeleteFinishLocationAsync(string entryId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<RPFinishFacialType> SaveFinishFacialTypeAsync(RPFinishFacialType entry, CancellationToken cancellationToken = default) => Task.FromResult(entry);
        public Task<IReadOnlyList<RPFinishFacialType>> ListFinishFacialTypesAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RPFinishFacialType>>([]);
        public Task<bool> DeleteFinishFacialTypeAsync(string entryId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<RPFinishReceptivityLevel> SaveFinishReceptivityLevelAsync(RPFinishReceptivityLevel entry, CancellationToken cancellationToken = default) => Task.FromResult(entry);
        public Task<IReadOnlyList<RPFinishReceptivityLevel>> ListFinishReceptivityLevelsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RPFinishReceptivityLevel>>([]);
        public Task<bool> DeleteFinishReceptivityLevelAsync(string entryId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<RPFinishHisControlLevel> SaveFinishHisControlLevelAsync(RPFinishHisControlLevel entry, CancellationToken cancellationToken = default) => Task.FromResult(entry);
        public Task<IReadOnlyList<RPFinishHisControlLevel>> ListFinishHisControlLevelsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RPFinishHisControlLevel>>([]);
        public Task<bool> DeleteFinishHisControlLevelAsync(string entryId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<RPFinishTransitionAction> SaveFinishTransitionActionAsync(RPFinishTransitionAction entry, CancellationToken cancellationToken = default) => Task.FromResult(entry);
        public Task<IReadOnlyList<RPFinishTransitionAction>> ListFinishTransitionActionsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RPFinishTransitionAction>>([]);
        public Task<bool> DeleteFinishTransitionActionAsync(string entryId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class RecordingDebugEventSink : IRolePlayDebugEventSink
    {
        public List<RolePlayDebugEventRecord> Records { get; } = [];

        public Task WriteAsync(RolePlayDebugEventRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }
}
