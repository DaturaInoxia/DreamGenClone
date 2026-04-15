namespace DreamGenClone.Web.Application.StoryAnalysis;

public interface ICharacterStatPresetImportService
{
    Task<CharacterStatPresetImportResult> ImportAsync(CancellationToken cancellationToken = default);
}
