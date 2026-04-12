using System.Text.RegularExpressions;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed partial class ThemeDefinitionParser : IThemeDefinitionParser
{
    public ThemeDefinitionDocument Parse(string filePath, string rawContent)
    {
        var warnings = new List<string>();
        var fileName = Path.GetFileName(filePath);

        var id = GetValue(rawContent, IdPattern());
        if (string.IsNullOrWhiteSpace(id))
        {
            warnings.Add("Missing metadata field: ID");
            id = ToKebabCase(Path.GetFileNameWithoutExtension(filePath));
        }

        var label = GetValue(rawContent, LabelPattern());
        if (string.IsNullOrWhiteSpace(label))
        {
            warnings.Add("Missing metadata field: Label");
            label = Path.GetFileNameWithoutExtension(filePath);
        }

        var category = GetValue(rawContent, CategoryPattern());
        if (string.IsNullOrWhiteSpace(category))
        {
            warnings.Add("Missing metadata field: Category");
            category = "Uncategorized";
        }

        var weightText = GetValue(rawContent, WeightPattern());
        var weight = 0;
        if (string.IsNullOrWhiteSpace(weightText) || !int.TryParse(weightText, out weight))
        {
            warnings.Add("Missing or invalid metadata field: Weight");
        }

        return new ThemeDefinitionDocument
        {
            Id = id.Trim(),
            Label = label.Trim(),
            Category = category.Trim(),
            Weight = weight,
            SourceFilePath = filePath,
            SourceFileName = fileName,
            RawContent = rawContent,
            ParseWarnings = warnings
        };
    }

    private static string? GetValue(string content, Regex regex)
    {
        var match = regex.Match(content);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups["value"].Value.Trim();
    }

    private static string ToKebabCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var normalized = Regex.Replace(input.Trim(), "[^A-Za-z0-9]+", "-");
        normalized = Regex.Replace(normalized, "-+", "-").Trim('-');
        return normalized.ToLowerInvariant();
    }

    [GeneratedRegex(@"\*\*ID:\*\*\s*`?(?<value>[^`\r\n]+)`?", RegexOptions.IgnoreCase)]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"\*\*Label:\*\*\s*(?<value>[^\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex LabelPattern();

    [GeneratedRegex(@"\*\*Category:\*\*\s*(?<value>[^\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CategoryPattern();

    [GeneratedRegex(@"\*\*Weight:\*\*\s*(?<value>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex WeightPattern();
}
