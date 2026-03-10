namespace DreamGenClone.Web.Application.Scenarios;

using DreamGenClone.Web.Domain.Scenarios;

/// <summary>
/// Service for estimating token counts in scenarios.
/// Uses heuristic estimation (approximately 4 characters per token).
/// </summary>
public interface IScenarioTokenCounter
{
    /// <summary>
    /// Calculate estimated token count for a scenario.
    /// </summary>
    int CalculateTokenCount(Scenario scenario);
    
    /// <summary>
    /// Calculate estimated token count from text.
    /// </summary>
    int CalculateTokenCount(string? text);
}

/// <summary>
/// Implementation of token counting for scenarios.
/// </summary>
public class ScenarioTokenCounter : IScenarioTokenCounter
{
    // Approximate 4 characters per token (common LLM heuristic)
    private const int CharactersPerToken = 4;
    
    public int CalculateTokenCount(Scenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        
        var tokenCount = 0;
        
        // Scenario metadata
        tokenCount += CalculateTokenCount(scenario.Name);
        tokenCount += CalculateTokenCount(scenario.Description);
        
        // Plot
        tokenCount += CalculateTokenCount(scenario.Plot.Title);
        tokenCount += CalculateTokenCount(scenario.Plot.Description);
        tokenCount += scenario.Plot.Conflicts.Sum(c => CalculateTokenCount(c));
        tokenCount += scenario.Plot.Goals.Sum(g => CalculateTokenCount(g));
        
        // Setting
        tokenCount += CalculateTokenCount(scenario.Setting.WorldDescription);
        tokenCount += CalculateTokenCount(scenario.Setting.TimeFrame);
        tokenCount += scenario.Setting.EnvironmentalDetails.Sum(d => CalculateTokenCount(d));
        tokenCount += scenario.Setting.WorldRules.Sum(r => CalculateTokenCount(r));
        
        // Style
        tokenCount += CalculateTokenCount(scenario.Style.Tone);
        tokenCount += CalculateTokenCount(scenario.Style.WritingStyle);
        tokenCount += CalculateTokenCount(scenario.Style.PointOfView);
        tokenCount += scenario.Style.StyleGuidelines.Sum(g => CalculateTokenCount(g));
        
        // Entities
        foreach (var character in scenario.Characters)
        {
            tokenCount += CalculateTokenCount(character.Name);
            tokenCount += CalculateTokenCount(character.Description);
            tokenCount += CalculateTokenCount(character.Role);
        }
        
        foreach (var location in scenario.Locations)
        {
            tokenCount += CalculateTokenCount(location.Name);
            tokenCount += CalculateTokenCount(location.Description);
        }
        
        foreach (var obj in scenario.Objects)
        {
            tokenCount += CalculateTokenCount(obj.Name);
            tokenCount += CalculateTokenCount(obj.Description);
        }
        
        // Openings and Examples
        foreach (var opening in scenario.Openings)
        {
            tokenCount += CalculateTokenCount(opening.Title);
            tokenCount += CalculateTokenCount(opening.Text);
        }
        
        foreach (var example in scenario.Examples)
        {
            tokenCount += CalculateTokenCount(example.Title);
            tokenCount += CalculateTokenCount(example.Text);
        }
        
        return tokenCount;
    }
    
    public int CalculateTokenCount(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }
        
        var tokens = (text.Length + CharactersPerToken - 1) / CharactersPerToken;
        return tokens;
    }
}
