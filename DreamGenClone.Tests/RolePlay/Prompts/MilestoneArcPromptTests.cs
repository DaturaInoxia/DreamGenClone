using System.Reflection;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay.Prompts;

/// <summary>
/// T074: Contract tests for the phase-milestone and arc-completion enrichment prompts.
/// Asserts the refined prompt structure: expanded system block, labeled ## INSTRUCTIONS,
/// first-person directives, stat context, and per-character memory history for arcs.
/// </summary>
public sealed class MilestoneArcPromptTests
{
    private static readonly Type HandlerType = typeof(EncounterSummaryJobHandler);

    private static string InvokeString(string methodName, params object?[] args)
    {
        var method = HandlerType.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method {methodName} not found.");
        var result = method.Invoke(null, args) as string;
        return result ?? throw new InvalidOperationException($"Method {methodName} returned null.");
    }

    private static EncounterSummaryRecord MakeRecord(
        string characterId = "Becky",
        EncounterSummaryType type = EncounterSummaryType.PhaseMilestone,
        int encounterNumber = 0,
        int cycleIndex = 0,
        string? llmSummary = null,
        string statsJson = """{"desire":60,"restraint":30}""")
    {
        return new EncounterSummaryRecord
        {
            Id = $"rec-{characterId}-{type}-{cycleIndex}-{encounterNumber}",
            SessionId = "sess-1",
            CharacterId = characterId,
            SummaryType = type,
            CycleIndex = cycleIndex,
            FromPhase = NarrativePhase.Committed,
            ToPhase = NarrativePhase.Approaching,
            OccurredUtc = DateTime.UtcNow,
            SceneLocation = "The Bedroom",
            EncounterNumber = encounterNumber,
            LlmSummary = llmSummary,
            CharacterStatsSnapshotJson = statsJson
        };
    }

    [Fact]
    public void MilestonePrompt_HasLabeledInstructions_FirstPerson_AndStats()
    {
        var prompt = InvokeString(
            "BuildMilestonePrompt",
            MakeRecord(),
            new List<string> { "[Dialogue] Dean: the evening stretched on." });

        Assert.Contains("## INSTRUCTIONS", prompt);
        Assert.Contains("first person", prompt);
        Assert.Contains("4-6 sentences", prompt);
        Assert.Contains("Character stats at transition: Desire 60, Restraint 30", prompt);
    }

    [Fact]
    public void MilestonePrompt_HasFiveDimensions()
    {
        var prompt = InvokeString(
            "BuildMilestonePrompt",
            MakeRecord(),
            new List<string>());

        Assert.Contains("1. What happened", prompt);
        Assert.Contains("2. What they felt", prompt);
        Assert.Contains("3. Who was involved", prompt);
        Assert.Contains("4. What shifted", prompt);
        Assert.Contains("5. What stands out", prompt);
    }

    [Fact]
    public void ArcPrompt_HasLabeledInstructions_FirstPerson_AndStats()
    {
        var prompt = InvokeString(
            "BuildArcCompletionPrompt",
            MakeRecord(type: EncounterSummaryType.ArcCompletion, cycleIndex: 1),
            new List<EncounterSummaryRecord>(),
            new List<string> { "[Dialogue] Dean: text" });

        Assert.Contains("## INSTRUCTIONS", prompt);
        Assert.Contains("first person", prompt);
        Assert.Contains("5-7 sentences", prompt);
        Assert.Contains("Character stats at close: Desire 60, Restraint 30", prompt);
    }

    [Fact]
    public void ArcPrompt_InjectsCharactersFullMemoryHistory()
    {
        var arcRecord = MakeRecord(type: EncounterSummaryType.ArcCompletion, cycleIndex: 2);
        var memories = new List<EncounterSummaryRecord>
        {
            MakeRecord(type: EncounterSummaryType.PhaseMilestone, cycleIndex: 1,
                llmSummary: "Becky felt the tension build through the evening."),
            MakeRecord(type: EncounterSummaryType.EncounterCompletion, cycleIndex: 1, encounterNumber: 1,
                llmSummary: "Becky remembers the first time they came together.")
        };

        var prompt = InvokeString("BuildArcCompletionPrompt", arcRecord, memories, new List<string>());

        Assert.Contains("memory records so far", prompt);
        Assert.Contains("[Phase Committed → Approaching, Arc 2]", prompt);
        Assert.Contains("[Encounter 1, Arc 2]", prompt);
        Assert.Contains("Becky remembers the first time they came together.", prompt);
    }

    [Fact]
    public void ArcPrompt_FallsBackToInteractions_WhenNoMemoriesExist()
    {
        var arcRecord = MakeRecord(type: EncounterSummaryType.ArcCompletion, cycleIndex: 1);
        var interactions = new List<string> { "[Narrative] The arc unfolded." };

        var prompt = InvokeString(
            "BuildArcCompletionPrompt",
            arcRecord,
            new List<EncounterSummaryRecord>(),
            interactions);

        Assert.Contains("Arc interactions (in order):", prompt);
        Assert.Contains("[Narrative] The arc unfolded.", prompt);
    }

    [Fact]
    public void CharacterMemorySet_IsPerCharacter_ExcludesCurrentAndNullSummaries()
    {
        var current = MakeRecord(type: EncounterSummaryType.ArcCompletion, cycleIndex: 2);
        var all = new List<EncounterSummaryRecord>
        {
            MakeRecord(type: EncounterSummaryType.PhaseMilestone, cycleIndex: 0, llmSummary: "Becky's phase."),
            MakeRecord(type: EncounterSummaryType.EncounterCompletion, cycleIndex: 0, encounterNumber: 1, llmSummary: "Becky's encounter."),
            MakeRecord(type: EncounterSummaryType.ArcCompletion, cycleIndex: 1, llmSummary: "Becky's prior arc."),
            MakeRecord(characterId: "Dean", type: EncounterSummaryType.PhaseMilestone, cycleIndex: 0, llmSummary: "Dean's phase."),
            current,
            MakeRecord(type: EncounterSummaryType.PhaseMilestone, cycleIndex: 2, llmSummary: null)
        };

        var method = HandlerType.GetMethod("BuildCharacterMemorySet", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("BuildCharacterMemorySet not found.");
        var result = (IReadOnlyList<EncounterSummaryRecord>?)method.Invoke(null, new object[] { all, current });

        Assert.NotNull(result);
        Assert.DoesNotContain(result!, r => r.Id == current.Id);
        Assert.DoesNotContain(result!, r => r.CharacterId == "Dean");
        Assert.All(result!, r => Assert.NotNull(r.LlmSummary));
        Assert.Equal(3, result!.Count);
    }
}
