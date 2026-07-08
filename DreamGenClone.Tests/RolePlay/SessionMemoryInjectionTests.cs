using System.Reflection;
using System.Text;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Tests for the <c>InjectSessionMemoryBlock</c> static method in
/// <see cref="RolePlayContinuationService"/> via reflection.
/// </summary>
public sealed class SessionMemoryInjectionTests
{
    // ── helper: invoke private static via reflection ──────────────────────

    private static string Inject(
        List<EncounterSummaryRecord> summaries,
        int effectiveMilestones,
        int effectiveArcCompletions,
        int currentCycleIndex,
        int effectiveEncounterCompletions = 5)
    {
        var method = typeof(RolePlayContinuationService)
            .GetMethod("InjectSessionMemoryBlock",
                BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("InjectSessionMemoryBlock not found via reflection");

        var sb = new StringBuilder();
        method.Invoke(null, [sb, summaries, effectiveMilestones, effectiveArcCompletions, effectiveEncounterCompletions, currentCycleIndex]);
        return sb.ToString();
    }

    private static EncounterSummaryRecord MilestoneRecord(
        string charId,
        int cycleIndex = 0,
        string templateSummary = "template text",
        string? llmSummary = null,
        DateTime? occurredUtc = null) => new()
    {
        Id              = Guid.NewGuid().ToString("N"),
        SessionId       = "sess",
        CharacterId     = charId,
        SummaryType     = EncounterSummaryType.PhaseMilestone,
        CycleIndex      = cycleIndex,
        FromPhase       = NarrativePhase.Committed,
        ToPhase         = NarrativePhase.Approaching,
        OccurredUtc     = occurredUtc ?? DateTime.UtcNow,
        TemplateSummary = templateSummary,
        LlmSummary      = llmSummary
    };

    private static EncounterSummaryRecord ArcRecord(
        string charId,
        int cycleIndex = 0,
        string templateSummary = "arc template text",
        string? llmSummary = null,
        DateTime? occurredUtc = null) => new()
    {
        Id              = Guid.NewGuid().ToString("N"),
        SessionId       = "sess",
        CharacterId     = charId,
        SummaryType     = EncounterSummaryType.ArcCompletion,
        CycleIndex      = cycleIndex,
        FromPhase       = NarrativePhase.Climax,
        ToPhase         = NarrativePhase.Reset,
        OccurredUtc     = occurredUtc ?? DateTime.UtcNow,
        TemplateSummary = templateSummary,
        LlmSummary      = llmSummary
    };

    // ── T017 ─────────────────────────────────────────────────────────────

    [Fact]
    public void InjectBlock_NoSummaries_BlockOmitted()
    {
        var output = Inject([], effectiveMilestones: 5, effectiveArcCompletions: 10, currentCycleIndex: 0);

        Assert.DoesNotContain("Session Memory:", output);
        Assert.Empty(output.Trim());
    }

    [Fact]
    public void InjectBlock_LlmSummaryPreferredOverTemplate()
    {
        var record = MilestoneRecord("char-a", templateSummary: "template prose", llmSummary: "llm prose");
        var summaries = new List<EncounterSummaryRecord> { record };

        var output = Inject(summaries, effectiveMilestones: 5, effectiveArcCompletions: 10, currentCycleIndex: 0);

        Assert.Contains("llm prose", output);
        Assert.DoesNotContain("template prose", output);
    }

    // ── T021 ─────────────────────────────────────────────────────────────

    [Fact]
    public void InjectBlock_ArcCompletionsRenderedBeforeMilestones()
    {
        var arc       = ArcRecord("char-a",  cycleIndex: 0, templateSummary: "arc summary");
        var milestone = MilestoneRecord("char-a", cycleIndex: 1, templateSummary: "phase summary");

        var summaries = new List<EncounterSummaryRecord> { milestone, arc };

        var output = Inject(summaries, effectiveMilestones: 5, effectiveArcCompletions: 10, currentCycleIndex: 1);

        var arcIdx       = output.IndexOf("Arc", StringComparison.Ordinal);
        var milestoneIdx = output.IndexOf("Committed", StringComparison.Ordinal);

        Assert.True(arcIdx >= 0,       "Arc header not found");
        Assert.True(milestoneIdx >= 0, "Milestone phase header not found");
        Assert.True(arcIdx < milestoneIdx, "Arc completions should appear before phase milestones");
    }

    // ── T023 ─────────────────────────────────────────────────────────────

    [Fact]
    public void InjectBlock_MaxMilestonesEnforced()
    {
        var base_time = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var summaries = new List<EncounterSummaryRecord>
        {
            MilestoneRecord("char-a", cycleIndex: 0, occurredUtc: base_time.AddMinutes(1)),
            MilestoneRecord("char-a", cycleIndex: 0, occurredUtc: base_time.AddMinutes(2)),
            MilestoneRecord("char-a", cycleIndex: 0, occurredUtc: base_time.AddMinutes(3)),
            MilestoneRecord("char-a", cycleIndex: 0, occurredUtc: base_time.AddMinutes(4)),
        };

        var output = Inject(summaries, effectiveMilestones: 2, effectiveArcCompletions: 10, currentCycleIndex: 0);

        // Count occurrences of the milestone header
        var count = CountOccurrences(output, "Committed →");
        Assert.Equal(2, count);
    }

    // ── T024 ─────────────────────────────────────────────────────────────

    [Fact]
    public void InjectBlock_MilestonesFilteredToCurrentArcOnly()
    {
        var priorArc   = MilestoneRecord("char-a", cycleIndex: 0, templateSummary: "prior arc milestone");
        var currentArc = MilestoneRecord("char-a", cycleIndex: 1, templateSummary: "current arc milestone");

        var summaries = new List<EncounterSummaryRecord> { priorArc, currentArc };

        // currentCycleIndex = 1 → only the current arc milestone should appear
        var output = Inject(summaries, effectiveMilestones: 5, effectiveArcCompletions: 10, currentCycleIndex: 1);

        Assert.Contains("current arc milestone", output);
        Assert.DoesNotContain("prior arc milestone", output);
    }

    // ── T026 ─────────────────────────────────────────────────────────────

    [Fact]
    public void InjectBlock_PerSessionOverrideUsedWhenPresent()
    {
        // Simulate: global = 5, per-session override = 8
        // Create 8 milestones in current arc
        var base_time = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var summaries = Enumerable.Range(0, 8)
            .Select(i => MilestoneRecord("char-a", cycleIndex: 0, occurredUtc: base_time.AddMinutes(i)))
            .ToList<EncounterSummaryRecord>();

        // effectiveMilestones = 8 (per-session override wins over global 5)
        var output = Inject(summaries, effectiveMilestones: 8, effectiveArcCompletions: 10, currentCycleIndex: 0);

        var count = CountOccurrences(output, "Committed →");
        Assert.Equal(8, count);
    }

    // ── T027 ─────────────────────────────────────────────────────────────

    [Fact]
    public void InjectBlock_MaxArcCompletionsEnforced()
    {
        var base_time = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var summaries = Enumerable.Range(0, 5)
            .Select(i => ArcRecord("char-a", cycleIndex: i, occurredUtc: base_time.AddDays(i)))
            .ToList<EncounterSummaryRecord>();

        // MaxArcCompletionsToInject = 3, so only 3 arc entries should appear
        var output = Inject(summaries, effectiveMilestones: 5, effectiveArcCompletions: 3, currentCycleIndex: 5);

        var count = CountOccurrences(output, "Arc ");
        Assert.Equal(3, count);
    }

    // ── utility ──────────────────────────────────────────────────────────

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0, start = 0;
        while ((start = text.IndexOf(pattern, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += pattern.Length;
        }
        return count;
    }
}
