using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Unit tests for PromptTextTruncation.TrimInteractionHistoryBlock
/// </summary>
public class PromptTextTruncationTests
{
    private const string StartMarker = "Session Memory below = summarized past events for long-term context:";
    private const string EndMarker = "\nSession Memory:";

    private static string MakePrompt(string before, string middle, string after) =>
        $"{before}\n{StartMarker}\n{middle}{EndMarker}\n{after}";

    #region Basic Functionality

    [Fact]
    public void TrimInteractionHistoryBlock_WithLongHistory_TrimsToFirstAndLastN()
    {
        // Arrange
        var historyContent = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"[User] User message {i}: This is a longer message to ensure we exceed the threshold"));
        var prompt = MakePrompt("System preamble", historyContent, "Next section");
        var edgeSize = 200;

        // Act
        var result = PromptTextTruncation.TrimInteractionHistoryBlock(prompt, edgeSize);

        // Assert
        Assert.Contains("System preamble", result);
        Assert.Contains(StartMarker, result);
        Assert.Contains(EndMarker.TrimStart('\n'), result);
        Assert.Contains("REMOVED FOR BREVITY", result);
    }

    [Fact]
    public void TrimInteractionHistoryBlock_WithShortHistory_ReturnsUnchanged()
    {
        // Arrange
        var shortHistory = "[User] Hello\n[Assistant] Hi there";
        var prompt = MakePrompt("System preamble", shortHistory, "Next section");
        var edgeSize = 200;

        // Act
        var result = PromptTextTruncation.TrimInteractionHistoryBlock(prompt, edgeSize);

        // Assert
        Assert.Equal(prompt, result);
        Assert.DoesNotContain("REMOVED FOR BREVITY", result);
    }

    [Fact]
    public void TrimInteractionHistoryBlock_WithNoHistoryMarker_ReturnsUnchanged()
    {
        // Arrange
        var prompt = "System preamble\n\nSome other content\n\nNext section";
        var edgeSize = 200;

        // Act
        var result = PromptTextTruncation.TrimInteractionHistoryBlock(prompt, edgeSize);

        // Assert
        Assert.Equal(prompt, result);
    }

    [Fact]
    public void TrimInteractionHistoryBlock_WithNullInput_ReturnsNull()
    {
        // Act
        var result = PromptTextTruncation.TrimInteractionHistoryBlock(null!, 200);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void TrimInteractionHistoryBlock_WithEmptyInput_ReturnsEmpty()
    {
        // Act
        var result = PromptTextTruncation.TrimInteractionHistoryBlock("", 200);

        // Assert
        Assert.Equal("", result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void TrimInteractionHistoryBlock_WithHistoryExactlyAtThreshold_ReturnsUnchanged()
    {
        var historyContent = new string('x', 398);
        var prompt = MakePrompt("", historyContent, "Next");

        var result = PromptTextTruncation.TrimInteractionHistoryBlock(prompt, 200);

        Assert.Equal(prompt, result);
    }

    [Fact]
    public void TrimInteractionHistoryBlock_WithHistoryJustOverThreshold_Trims()
    {
        var historyContent = new string('x', 401);
        var prompt = MakePrompt("", historyContent, "Next");

        var result = PromptTextTruncation.TrimInteractionHistoryBlock(prompt, 200);

        Assert.Contains("REMOVED FOR BREVITY", result);
    }

    [Fact]
    public void TrimInteractionHistoryBlock_WithCustomEdgeSize_RespectsParameter()
    {
        var historyContent = new string('x', 1000);
        var prompt = MakePrompt("", historyContent, "Next");
        var edgeSize = 100;

        var result = PromptTextTruncation.TrimInteractionHistoryBlock(prompt, edgeSize);

        Assert.Contains("REMOVED FOR BREVITY", result);
    }

    [Fact]
    public void TrimInteractionHistoryBlock_WithZeroEdgeSize_ReturnsUnchanged()
    {
        var prompt = MakePrompt("", "Some content", "Next");

        var result = PromptTextTruncation.TrimInteractionHistoryBlock(prompt, 0);

        Assert.Equal(prompt, result);
    }

    [Fact]
    public void TrimInteractionHistoryBlock_WithNegativeEdgeSize_ReturnsUnchanged()
    {
        var prompt = MakePrompt("", "Some content", "Next");

        var result = PromptTextTruncation.TrimInteractionHistoryBlock(prompt, -100);

        Assert.Equal(prompt, result);
    }

    #endregion

    #region Preservation of Non-History Sections

    [Fact]
    public void TrimInteractionHistoryBlock_PreservesSystemPreamble()
    {
        var systemPreamble = "You are a helpful assistant specialized in creative writing.";
        var historyContent = new string('x', 1000);
        var prompt = MakePrompt(systemPreamble, historyContent, "Next");

        var result = PromptTextTruncation.TrimInteractionHistoryBlock(prompt, 200);

        Assert.StartsWith(systemPreamble, result);
    }

    [Fact]
    public void TrimInteractionHistoryBlock_PreservesAllSectionsAfterHistory()
    {
        var historyContent = new string('x', 1000);
        var afterContent = "## Scenario Context\nThe user is exploring a fantasy world.\n## Characters\n- Protagonist: Brave hero\n- Antagonist: Dark lord";
        var prompt = MakePrompt("System preamble", historyContent, afterContent);

        var result = PromptTextTruncation.TrimInteractionHistoryBlock(prompt, 200);

        Assert.Contains(afterContent, result);
    }

    [Fact]
    public void TrimInteractionHistoryBlock_PreservesHistoryHeader()
    {
        var historyContent = new string('x', 1000);
        var prompt = MakePrompt("System preamble", historyContent, "Next");

        var result = PromptTextTruncation.TrimInteractionHistoryBlock(prompt, 200);

        Assert.Contains(StartMarker, result);
    }

    #endregion

    #region Realistic Prompt Structure

    [Fact]
    public void TrimInteractionHistoryBlock_WithRealisticPromptStructure_TrimsCorrectly()
    {
        // Arrange - simulate a realistic prompt with multiple sections
        var systemPreamble = "You are DreamGen, a creative writing assistant.";
        var scenarioContext = "## Scenario\nA fantasy adventure in the realm of Eldoria.";
        var characterDescriptions = "## Characters\n- Hero: A brave warrior\n- Mentor: A wise sage";
        var injectedDirectives = "## Directives\n- Maintain consistent tone\n- Advance the plot";
        var currentTurnInstruction = "## Current Turn\nContinue the narrative from the hero's perspective.";
        
        // Create realistic interaction history with multiple turns
        var historyLines = new List<string>();
        for (int i = 1; i <= 50; i++)
        {
            historyLines.Add($"[User] User input {i}: The hero approaches the ancient castle.");
            historyLines.Add($"[Assistant] Assistant response {i}: The castle looms before you, its towers reaching toward the stormy sky.");
        }
        var historyContent = string.Join("\n", historyLines);
        
        var prompt = MakePrompt($"{systemPreamble}\n\n{scenarioContext}\n\n{characterDescriptions}", historyContent, $"{injectedDirectives}\n\n{currentTurnInstruction}");

        // Act
        var result = PromptTextTruncation.TrimInteractionHistoryBlock(prompt, 200);

        // Assert - all non-history sections preserved
        Assert.Contains(systemPreamble, result);
        Assert.Contains(scenarioContext, result);
        Assert.Contains(characterDescriptions, result);
        Assert.Contains(injectedDirectives, result);
        Assert.Contains(currentTurnInstruction, result);
        Assert.Contains(StartMarker, result);
        Assert.Contains(EndMarker.TrimStart('\n'), result);

        // History is truncated
        Assert.Contains("REMOVED FOR BREVITY", result);

        // Result is significantly shorter than original
        Assert.True(result.Length < prompt.Length * 0.5);
    }

    #endregion

    #region Boundary Detection

    [Fact]
    public void TrimInteractionHistoryBlock_WithNoTrailingSessionMemory_ReturnsUnchanged()
    {
        var historyContent = new string('x', 1000);
        var prompt = $"{StartMarker}\n{historyContent}";

        var result = PromptTextTruncation.TrimInteractionHistoryBlock(prompt, 200);

        Assert.Equal(prompt, result);
    }

    #endregion
}
