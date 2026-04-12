using DreamGenClone.Application.StoryAnalysis.Models;

namespace DreamGenClone.Application.StoryAnalysis;

public interface IThemeDefinitionParser
{
    ThemeDefinitionDocument Parse(string filePath, string rawContent);
}
