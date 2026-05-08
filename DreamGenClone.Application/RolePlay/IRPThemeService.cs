using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public sealed record RPThemeImportFile(string SourcePath, string MarkdownContent);

public sealed class RPThemeImportResult
{
    public string SourcePath { get; init; } = string.Empty;
    public string ThemeId { get; init; } = string.Empty;
    public bool Imported { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public string? Error { get; init; }
}

public interface IRPThemeService
{
    public const string GlobalThemeLibraryProfileId = "global-theme-library";

    Task<RPThemeProfile> SaveProfileAsync(RPThemeProfile profile, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RPThemeProfile>> ListProfilesAsync(CancellationToken cancellationToken = default);
    Task<RPThemeProfile?> GetProfileAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> DeleteProfileAsync(string id, CancellationToken cancellationToken = default);

    Task<RPTheme> SaveThemeAsync(RPTheme theme, CancellationToken cancellationToken = default);
    Task<RPTheme> CloneThemeAsync(string sourceThemeId, string newThemeId, string newThemeLabel, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RPTheme>> ListThemesAsync(bool includeDisabled = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RPTheme>> ListThemesByProfileAsync(string profileId, bool includeDisabled = false, CancellationToken cancellationToken = default);
    Task<RPTheme?> GetThemeAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> DeleteThemeAsync(string id, CancellationToken cancellationToken = default);

    Task<RPThemeProfileThemeAssignment> SaveProfileAssignmentAsync(RPThemeProfileThemeAssignment assignment, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RPThemeProfileThemeAssignment>> ListProfileAssignmentsAsync(string profileId, CancellationToken cancellationToken = default);
    Task<bool> DeleteProfileAssignmentAsync(string assignmentId, CancellationToken cancellationToken = default);

    Task<RPFinishingMoveMatrixRow> SaveFinishingMoveMatrixRowAsync(RPFinishingMoveMatrixRow row, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RPFinishingMoveMatrixRow>> ListFinishingMoveMatrixRowsAsync(CancellationToken cancellationToken = default);
    Task<bool> DeleteFinishingMoveMatrixRowAsync(string rowId, CancellationToken cancellationToken = default);
    Task<int> ImportFinishingMoveMatrixRowsFromJsonAsync(
        string json,
        bool replaceExisting = false,
        CancellationToken cancellationToken = default);

    Task<RPSteerPositionMatrixRow> SaveSteerPositionMatrixRowAsync(RPSteerPositionMatrixRow row, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RPSteerPositionMatrixRow>> ListSteerPositionMatrixRowsAsync(CancellationToken cancellationToken = default);
    Task<bool> DeleteSteerPositionMatrixRowAsync(string rowId, CancellationToken cancellationToken = default);
    Task<int> ImportSteerPositionMatrixRowsFromJsonAsync(
        string json,
        bool replaceExisting = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RPThemeImportResult>> ImportFromMarkdownAsync(
        IReadOnlyList<RPThemeImportFile> files,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RPThemeImportResult>> SyncFromMarkdownDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default);

    Task TruncateRolePlayAndScenarioDataAsync(CancellationToken cancellationToken = default);

    // Finishing Move Catalog
    Task<RPFinishLocation> SaveFinishLocationAsync(RPFinishLocation entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RPFinishLocation>> ListFinishLocationsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default);
    Task<bool> DeleteFinishLocationAsync(string entryId, CancellationToken cancellationToken = default);

    Task<RPFinishFacialType> SaveFinishFacialTypeAsync(RPFinishFacialType entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RPFinishFacialType>> ListFinishFacialTypesAsync(bool includeDisabled = false, CancellationToken cancellationToken = default);
    Task<bool> DeleteFinishFacialTypeAsync(string entryId, CancellationToken cancellationToken = default);

    Task<RPFinishReceptivityLevel> SaveFinishReceptivityLevelAsync(RPFinishReceptivityLevel entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RPFinishReceptivityLevel>> ListFinishReceptivityLevelsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default);
    Task<bool> DeleteFinishReceptivityLevelAsync(string entryId, CancellationToken cancellationToken = default);

    Task<RPFinishHisControlLevel> SaveFinishHisControlLevelAsync(RPFinishHisControlLevel entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RPFinishHisControlLevel>> ListFinishHisControlLevelsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default);
    Task<bool> DeleteFinishHisControlLevelAsync(string entryId, CancellationToken cancellationToken = default);

    Task<RPFinishTransitionAction> SaveFinishTransitionActionAsync(RPFinishTransitionAction entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RPFinishTransitionAction>> ListFinishTransitionActionsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default);
    Task<bool> DeleteFinishTransitionActionAsync(string entryId, CancellationToken cancellationToken = default);
}
