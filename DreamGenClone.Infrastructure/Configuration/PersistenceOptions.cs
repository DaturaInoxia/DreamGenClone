namespace DreamGenClone.Infrastructure.Configuration;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    public string Provider { get; set; } = "SQLite";

    public string ConnectionString { get; set; } = "Data Source=data/dreamgenclone.db";

    public string TemplateImageRoot { get; set; } = "data/template-images";
}
