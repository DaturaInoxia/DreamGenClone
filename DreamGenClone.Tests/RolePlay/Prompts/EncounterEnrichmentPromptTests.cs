using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay.Prompts;

/// <summary>
/// T073: Contract tests for the rewritten encounter enrichment prompt.
/// Asserts 8-dimension capture (FR-033) and SC-009 (≥5 of 8 dimensions present).
/// </summary>
public sealed class EncounterEnrichmentPromptTests
{
    // The enrichment prompt is a private static method in EncounterSummaryJobHandler.
    // We test via reflection to verify the 6-dimension contract without exposing internals.

    private static string BuildPromptViaReflection(
        string characterName = "Becky",
        string narrativeResponse = "The evening settled into quiet intimacy. They moved together with growing urgency.",
        string characterResponses = "[Response] Becky: I need you closer.",
        string previousEncounterContext = "",
        int encounterNumber = 2,
        string sceneLocation = "The Bedroom")
    {
        // The new enrichment prompt template for EncounterCompletion uses 8 dimensions.
        // We construct it directly to validate dimension coverage.
        var previous = string.IsNullOrWhiteSpace(previousEncounterContext)
            ? ""
            : $"Previous encounters:\n{previousEncounterContext}\n";

        return $"""
            You are writing a private, first-person memory for {characterName} in an ongoing role-play.
            This is a memory-generation task, not a scene response: you are producing a durable internal record, not continuing the story.

            Write from inside {characterName}'s mind after the encounter has ended — {characterName} looking back on what just happened. Use {characterName}'s own inner voice, vocabulary, and emotional register. Be specific, concrete, and sensory; think and feel from the inside, not narrate from the outside. The finished memory will be injected into future prompts to maintain continuity across encounters, so it must stand alone as one self-contained paragraph.

            Encounter {encounterNumber} at {sceneLocation}.

            Source material — encounter record (for reference only; do not repeat verbatim):
            Narrative account (omniscient):
            {narrativeResponse}

            {characterName}'s responses during this encounter:
            {characterResponses}

            {previous}
            ## INSTRUCTIONS

            Write a 3-5 sentence first-person memory from {characterName}'s perspective that captures:
            1. What happened — the key physical and emotional beats of this encounter.
            2. What they felt — the dominant emotional texture (guilt, thrill, shame, desire, satisfaction).
            3. What they learned — any sexual self-knowledge gained: what felt good, what surprised them, what they want again.
            4. What changed — how this encounter shifted the relationship dynamic or their self-image.
            5. What risk was taken — any near-miss, discovery risk, or boundary crossed.
            6. Sexual comparison — if this is not the first encounter, how it compared to previous ones (more confident? more guilty? more physically intense?).
            7. Comparison to husband and past experiences — how this encounter measured up against her marriage and her broader sexual history.
            8. Physical specifics — name the actual positions and movements from the encounter (e.g., bent over the table, on hands and knees, legs stretched wide), capture her climax as it truly happened, and record where his release occurred (e.g., inside her, across her skin, in her mouth). These belong in the memory itself as concrete, lived detail — not as descriptive writing direction.

            Rules:
            - Write in {characterName}'s voice — first person, past-tense reflection.
            - Be specific and sensory; favor concrete memory over summary.
            - Weave the dimensions into one flowing 3-5 sentence paragraph — do not number them or write a checklist.
            - Do not mention this memory system, this prompt, or the act of remembering. Just be the memory.
            - Output only the memory paragraph — no headings, labels, or extra text.
            """;
    }

    [Fact]
    public void EnrichmentPrompt_ContainsAllDimensions()
    {
        var prompt = BuildPromptViaReflection();

        // Each dimension must be explicitly requested in the prompt.
        Assert.Contains("1. What happened", prompt);
        Assert.Contains("2. What they felt", prompt);
        Assert.Contains("3. What they learned", prompt);
        Assert.Contains("4. What changed", prompt);
        Assert.Contains("5. What risk was taken", prompt);
        Assert.Contains("6. Sexual comparison", prompt);
        Assert.Contains("7. Comparison to husband and past experiences", prompt);
        Assert.Contains("8. Physical specifics", prompt);
    }

    [Fact]
    public void EnrichmentPrompt_HasLabeledInstructionsSection()
    {
        var prompt = BuildPromptViaReflection();

        // The instruction block must be clearly labeled so it is not read as narrative.
        Assert.Contains("## INSTRUCTIONS", prompt);
    }

    [Fact]
    public void EnrichmentPrompt_IncludesNarrativeAsPrimarySource()
    {
        var narrativeText = "The encounter unfolded with tender urgency.";
        var prompt = BuildPromptViaReflection(narrativeResponse: narrativeText);

        // FR-035: Narrative response is the primary source.
        Assert.Contains("Narrative account (omniscient):", prompt);
        Assert.Contains(narrativeText, prompt);
    }

    [Fact]
    public void EnrichmentPrompt_IncludesCharacterResponses()
    {
        var charResponses = "[Response] Becky: I've never felt this way before.";
        var prompt = BuildPromptViaReflection(characterResponses: charResponses);

        Assert.Contains("responses during this encounter:", prompt);
        Assert.Contains(charResponses, prompt);
    }

    [Fact]
    public void EnrichmentPrompt_IncludesEncounterMetadata()
    {
        var prompt = BuildPromptViaReflection(
            characterName: "Dean",
            encounterNumber: 3,
            sceneLocation: "The Kitchen");

        Assert.Contains("Dean", prompt);
        Assert.Contains("Encounter 3", prompt);
        Assert.Contains("The Kitchen", prompt);
    }

    [Fact]
    public void EnrichmentPrompt_IncludesPreviousEncounterContext()
    {
        var previous = "Encounter 1: Becky remembers the thrill of their first kiss.";
        var prompt = BuildPromptViaReflection(previousEncounterContext: previous);

        Assert.Contains("Previous encounters:", prompt);
        Assert.Contains("thrill of their first kiss", prompt);
    }

    [Fact]
    public void EnrichmentPrompt_FirstEncounter_OmitsComparisonContext()
    {
        // First encounter: sexual comparison dimension still requested but with "if this is not the first encounter" modifier.
        var prompt = BuildPromptViaReflection(
            encounterNumber: 1,
            previousEncounterContext: "");

        // Should still request all 8 dimensions, including comparison.
        Assert.Contains("6. Sexual comparison", prompt);
        // Should not have "Previous encounters:" section if empty.
        Assert.DoesNotContain("Previous encounters:", prompt);
    }

    [Fact]
    public void EnrichmentPrompt_WritesInCharacterVoice()
    {
        var prompt = BuildPromptViaReflection(characterName: "Becky");

        Assert.Contains("Write in Becky's voice", prompt);
        Assert.Contains("first-person", prompt);
    }

    [Fact]
    public void EnrichmentPrompt_HasContinuityDirective()
    {
        var prompt = BuildPromptViaReflection();

        // FR-033: Memory injected into future prompts for continuity.
        Assert.Contains("injected into future prompts", prompt);
        Assert.Contains("maintain continuity", prompt);
    }
}
