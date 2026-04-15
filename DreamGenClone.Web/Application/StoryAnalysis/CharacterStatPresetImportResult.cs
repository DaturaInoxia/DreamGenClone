namespace DreamGenClone.Web.Application.StoryAnalysis;

public sealed class CharacterStatPresetImportResult
{
    public int CreatedCount { get; set; }

    public int UpdatedCount { get; set; }

    public int SkippedCount { get; set; }

    public List<string> Warnings { get; set; } = [];

    public int TotalProcessed => CreatedCount + UpdatedCount + SkippedCount;
}
