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
    Task<IReadOnlyList<RPTheme>> ListThemesAsync(bool includeDisabled = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RPTheme>> ListThemesByProfileAsync(string profileId, bool includeDisabled = false, CancellationToken cancellationToken = default);
    Task<RPTheme?> GetThemeAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> DeleteThemeAsync(string id, CancellationToken cancellationToken = default);

    Task<RPThemeProfileThemeAssignment> SaveProfileAssignmentAsync(RPThemeProfileThemeAssignment assignment, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RPThemeProfileThemeAssignment>> ListProfileAssignmentsAsync(string profileId, CancellationToken cancellationToken = default);
    Task<bool> DeleteProfileAssignmentAsync(string assignmentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RPThemeImportResult>> ImportFromMarkdownAsync(
        IReadOnlyList<RPThemeImportFile> files,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RPThemeImportResult>> SyncFromMarkdownDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default);

    Task TruncateRolePlayAndScenarioDataAsync(CancellationToken cancellationToken = default);
}
