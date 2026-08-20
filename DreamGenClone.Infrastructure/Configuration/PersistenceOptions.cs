namespace DreamGenClone.Infrastructure.Configuration;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    public string Provider { get; set; } = "SQLite";

    public string ConnectionString { get; set; } = "Data Source=data/dreamgenclone.db";

    public string TemplateImageRoot { get; set; } = "data/template-images";

    /// <summary>Root directory for generated RP scene images. Git-ignored alongside the dev DB.</summary>
    public string SceneImageRoot { get; set; } = "data/scene-images";
}
