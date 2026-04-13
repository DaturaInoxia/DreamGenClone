using System.Text.RegularExpressions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed partial class RPThemeService : IRPThemeService
{
    private readonly string _connectionString;
    private readonly ILogger<RPThemeService> _logger;
    private bool? _rpThemesHasProfileIdColumn;

    public RPThemeService(IOptions<PersistenceOptions> options, ILogger<RPThemeService> logger)
    {
        _connectionString = options.Value.ConnectionString;
        _logger = logger;
    }

    public async Task<RPThemeProfile> SaveProfileAsync(RPThemeProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        profile.Id = string.IsNullOrWhiteSpace(profile.Id) ? Guid.NewGuid().ToString("N") : profile.Id.Trim();
        profile.Name = (profile.Name ?? string.Empty).Trim();
        profile.Description = (profile.Description ?? string.Empty).Trim();
        profile.UpdatedUtc = DateTime.UtcNow;
        if (profile.CreatedUtc == default)
        {
            profile.CreatedUtc = profile.UpdatedUtc;
        }

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new ArgumentException("RP theme profile name is required.", nameof(profile));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RPThemeProfiles (Id, Name, Description, IsDefault, CreatedUtc, UpdatedUtc)
            VALUES ($id, $name, $description, $isDefault, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Description = excluded.Description,
                IsDefault = excluded.IsDefault,
                UpdatedUtc = excluded.UpdatedUtc;
            """;

        command.Parameters.AddWithValue("$id", profile.Id);
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$description", profile.Description);
        command.Parameters.AddWithValue("$isDefault", profile.IsDefault ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", profile.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", profile.UpdatedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return profile;
    }

    public async Task<IReadOnlyList<RPThemeProfile>> ListProfilesAsync(CancellationToken cancellationToken = default)
    {
        var profiles = new List<RPThemeProfile>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, IsDefault, CreatedUtc, UpdatedUtc FROM RPThemeProfiles ORDER BY Name";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            profiles.Add(new RPThemeProfile
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Description = reader.GetString(2),
                IsDefault = reader.GetInt32(3) == 1,
                CreatedUtc = DateTime.TryParse(reader.GetString(4), out var created) ? created : DateTime.UtcNow,
                UpdatedUtc = DateTime.TryParse(reader.GetString(5), out var updated) ? updated : DateTime.UtcNow
            });
        }

        return profiles;
    }

    public async Task<RPThemeProfile?> GetProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, IsDefault, CreatedUtc, UpdatedUtc FROM RPThemeProfiles WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new RPThemeProfile
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            Description = reader.GetString(2),
            IsDefault = reader.GetInt32(3) == 1,
            CreatedUtc = DateTime.TryParse(reader.GetString(4), out var created) ? created : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(5), out var updated) ? updated : DateTime.UtcNow
        };
    }

    public async Task<bool> DeleteProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RPThemeProfiles WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<RPTheme> SaveThemeAsync(RPTheme theme, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(theme);

        theme.Id = string.IsNullOrWhiteSpace(theme.Id) ? Guid.NewGuid().ToString("N") : theme.Id.Trim();
        theme.Label = (theme.Label ?? string.Empty).Trim();
        theme.Description = (theme.Description ?? string.Empty).Trim();
        theme.Category = (theme.Category ?? string.Empty).Trim();
        theme.Weight = Math.Clamp(theme.Weight, 1, 10);
        theme.UpdatedUtc = DateTime.UtcNow;
        if (theme.CreatedUtc == default)
        {
            theme.CreatedUtc = theme.UpdatedUtc;
        }

        if (string.IsNullOrWhiteSpace(theme.Label))
        {
            throw new ArgumentException("Theme label is required.", nameof(theme));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureGlobalThemeLibraryProfileAsync(connection, cancellationToken);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = tx;
            if (await RPThemesTableHasProfileIdAsync(connection, cancellationToken))
            {
                command.CommandText = """
                    INSERT INTO RPThemes (Id, ProfileId, Label, Description, Category, Weight, IsEnabled, CreatedUtc, UpdatedUtc)
                    VALUES ($id, $profileId, $label, $description, $category, $weight, $isEnabled, $createdUtc, $updatedUtc)
                    ON CONFLICT(Id) DO UPDATE SET
                        ProfileId = excluded.ProfileId,
                        Label = excluded.Label,
                        Description = excluded.Description,
                        Category = excluded.Category,
                        Weight = excluded.Weight,
                        IsEnabled = excluded.IsEnabled,
                        UpdatedUtc = excluded.UpdatedUtc;
                    """;
                command.Parameters.AddWithValue("$profileId", IRPThemeService.GlobalThemeLibraryProfileId);
            }
            else
            {
                command.CommandText = """
                    INSERT INTO RPThemes (Id, Label, Description, Category, Weight, IsEnabled, CreatedUtc, UpdatedUtc)
                    VALUES ($id, $label, $description, $category, $weight, $isEnabled, $createdUtc, $updatedUtc)
                    ON CONFLICT(Id) DO UPDATE SET
                        Label = excluded.Label,
                        Description = excluded.Description,
                        Category = excluded.Category,
                        Weight = excluded.Weight,
                        IsEnabled = excluded.IsEnabled,
                        UpdatedUtc = excluded.UpdatedUtc;
                    """;
            }
            command.Parameters.AddWithValue("$id", theme.Id);
            command.Parameters.AddWithValue("$label", theme.Label);
            command.Parameters.AddWithValue("$description", theme.Description);
            command.Parameters.AddWithValue("$category", theme.Category);
            command.Parameters.AddWithValue("$weight", theme.Weight);
            command.Parameters.AddWithValue("$isEnabled", theme.IsEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$createdUtc", theme.CreatedUtc.ToString("O"));
            command.Parameters.AddWithValue("$updatedUtc", theme.UpdatedUtc.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await ReplaceThemeChildrenAsync(connection, tx, theme, cancellationToken);

        await using (var deleteHierarchy = connection.CreateCommand())
        {
            deleteHierarchy.Transaction = tx;
            deleteHierarchy.CommandText = "DELETE FROM RPThemeRelationships WHERE ChildThemeId = $themeId";
            deleteHierarchy.Parameters.AddWithValue("$themeId", theme.Id);
            await deleteHierarchy.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(theme.ParentThemeId))
        {
            await using var insertHierarchy = connection.CreateCommand();
            insertHierarchy.Transaction = tx;
            insertHierarchy.CommandText = """
                INSERT INTO RPThemeRelationships (ParentThemeId, ChildThemeId, SortOrder)
                VALUES ($parentThemeId, $childThemeId, $sortOrder)
                ON CONFLICT(ParentThemeId, ChildThemeId) DO UPDATE SET SortOrder = excluded.SortOrder;
                """;
            insertHierarchy.Parameters.AddWithValue("$parentThemeId", theme.ParentThemeId);
            insertHierarchy.Parameters.AddWithValue("$childThemeId", theme.Id);
            insertHierarchy.Parameters.AddWithValue("$sortOrder", 0);
            await insertHierarchy.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return theme;
    }

    public async Task<IReadOnlyList<RPTheme>> ListThemesAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
    {
        var themes = new List<RPTheme>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = includeDisabled
            ? "SELECT Id, Label, Description, Category, Weight, IsEnabled, CreatedUtc, UpdatedUtc FROM RPThemes ORDER BY Label"
            : "SELECT Id, Label, Description, Category, Weight, IsEnabled, CreatedUtc, UpdatedUtc FROM RPThemes WHERE IsEnabled = 1 ORDER BY Label";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            themes.Add(new RPTheme
            {
                Id = reader.GetString(0),
                Label = reader.GetString(1),
                Description = reader.GetString(2),
                Category = reader.GetString(3),
                Weight = reader.GetInt32(4),
                IsEnabled = reader.GetInt32(5) == 1,
                CreatedUtc = DateTime.TryParse(reader.GetString(6), out var created) ? created : DateTime.UtcNow,
                UpdatedUtc = DateTime.TryParse(reader.GetString(7), out var updated) ? updated : DateTime.UtcNow
            });
        }

        foreach (var theme in themes)
        {
            theme.ParentThemeId = await LoadParentThemeIdAsync(connection, theme.Id, cancellationToken);
            theme.Keywords = await LoadThemeKeywordsAsync(connection, theme.Id, cancellationToken);
            theme.StatAffinities = await LoadThemeStatAffinitiesAsync(connection, theme.Id, cancellationToken);
            theme.PhaseGuidance = await LoadThemePhaseGuidanceAsync(connection, theme.Id, cancellationToken);
            theme.GuidancePoints = await LoadThemeGuidancePointsAsync(connection, theme.Id, cancellationToken);
        }

        return themes;
    }

    public async Task<IReadOnlyList<RPTheme>> ListThemesByProfileAsync(string profileId, bool includeDisabled = false, CancellationToken cancellationToken = default)
    {
        var themes = new List<RPTheme>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = includeDisabled
            ? """
                            SELECT DISTINCT t.Id, t.Label, t.Description, t.Category, t.Weight, t.IsEnabled, t.CreatedUtc, t.UpdatedUtc
              FROM RPThemes t
              INNER JOIN RPThemeProfileThemeAssignments a ON a.ThemeId = t.Id
              WHERE a.ProfileId = $profileId AND a.IsEnabled = 1
              ORDER BY t.Label
              """
            : """
                            SELECT DISTINCT t.Id, t.Label, t.Description, t.Category, t.Weight, t.IsEnabled, t.CreatedUtc, t.UpdatedUtc
              FROM RPThemes t
              INNER JOIN RPThemeProfileThemeAssignments a ON a.ThemeId = t.Id
              WHERE a.ProfileId = $profileId AND a.IsEnabled = 1 AND t.IsEnabled = 1
              ORDER BY t.Label
              """;
        command.Parameters.AddWithValue("$profileId", profileId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            themes.Add(new RPTheme
            {
                Id = reader.GetString(0),
                Label = reader.GetString(1),
                Description = reader.GetString(2),
                Category = reader.GetString(3),
                Weight = reader.GetInt32(4),
                IsEnabled = reader.GetInt32(5) == 1,
                CreatedUtc = DateTime.TryParse(reader.GetString(6), out var created) ? created : DateTime.UtcNow,
                UpdatedUtc = DateTime.TryParse(reader.GetString(7), out var updated) ? updated : DateTime.UtcNow
            });
        }

        foreach (var theme in themes)
        {
            theme.ParentThemeId = await LoadParentThemeIdAsync(connection, theme.Id, cancellationToken);
            theme.Keywords = await LoadThemeKeywordsAsync(connection, theme.Id, cancellationToken);
            theme.StatAffinities = await LoadThemeStatAffinitiesAsync(connection, theme.Id, cancellationToken);
            theme.PhaseGuidance = await LoadThemePhaseGuidanceAsync(connection, theme.Id, cancellationToken);
            theme.GuidancePoints = await LoadThemeGuidancePointsAsync(connection, theme.Id, cancellationToken);
        }

        return themes;
    }

    public async Task<RPTheme?> GetThemeAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Label, Description, Category, Weight, IsEnabled, CreatedUtc, UpdatedUtc FROM RPThemes WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var theme = new RPTheme
        {
            Id = reader.GetString(0),
            Label = reader.GetString(1),
            Description = reader.GetString(2),
            Category = reader.GetString(3),
            Weight = reader.GetInt32(4),
            IsEnabled = reader.GetInt32(5) == 1,
            CreatedUtc = DateTime.TryParse(reader.GetString(6), out var created) ? created : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(7), out var updated) ? updated : DateTime.UtcNow
        };

        theme.ParentThemeId = await LoadParentThemeIdAsync(connection, theme.Id, cancellationToken);
        theme.Keywords = await LoadThemeKeywordsAsync(connection, theme.Id, cancellationToken);
        theme.StatAffinities = await LoadThemeStatAffinitiesAsync(connection, theme.Id, cancellationToken);
        theme.PhaseGuidance = await LoadThemePhaseGuidanceAsync(connection, theme.Id, cancellationToken);
        theme.GuidancePoints = await LoadThemeGuidancePointsAsync(connection, theme.Id, cancellationToken);

        return theme;
    }

    public async Task<bool> DeleteThemeAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        await using (var checkCommand = connection.CreateCommand())
        {
            checkCommand.CommandText = "SELECT COUNT(*) FROM RPThemeProfileThemeAssignments WHERE ThemeId = $id";
            checkCommand.Parameters.AddWithValue("$id", id);
            var assignmentCount = Convert.ToInt64(await checkCommand.ExecuteScalarAsync(cancellationToken));
            if (assignmentCount > 0)
            {
                _logger.LogInformation("Skipped deleting RP theme {ThemeId} because it is referenced by {AssignmentCount} profile assignments.", id, assignmentCount);
                return false;
            }
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RPThemes WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<RPThemeProfileThemeAssignment> SaveProfileAssignmentAsync(RPThemeProfileThemeAssignment assignment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        assignment.Id = string.IsNullOrWhiteSpace(assignment.Id) ? Guid.NewGuid().ToString("N") : assignment.Id.Trim();
        assignment.ProfileId = (assignment.ProfileId ?? string.Empty).Trim();
        assignment.ThemeId = (assignment.ThemeId ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(assignment.ProfileId) || string.IsNullOrWhiteSpace(assignment.ThemeId))
        {
            throw new ArgumentException("ProfileId and ThemeId are required for assignment.", nameof(assignment));
        }

        assignment.Weight = assignment.Weight <= 0m
            ? GetDefaultWeightForTier(assignment.Tier)
            : Math.Clamp(assignment.Weight, 0m, 1m);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RPThemeProfileThemeAssignments (Id, ProfileId, ThemeId, Tier, Weight, SortOrder, IsEnabled)
            VALUES ($id, $profileId, $themeId, $tier, $weight, $sortOrder, $isEnabled)
            ON CONFLICT(Id) DO UPDATE SET
                ProfileId = excluded.ProfileId,
                ThemeId = excluded.ThemeId,
                Tier = excluded.Tier,
                Weight = excluded.Weight,
                SortOrder = excluded.SortOrder,
                IsEnabled = excluded.IsEnabled;
            """;
        command.Parameters.AddWithValue("$id", assignment.Id);
        command.Parameters.AddWithValue("$profileId", assignment.ProfileId);
        command.Parameters.AddWithValue("$themeId", assignment.ThemeId);
        command.Parameters.AddWithValue("$tier", assignment.Tier.ToString());
        command.Parameters.AddWithValue("$weight", assignment.Weight);
        command.Parameters.AddWithValue("$sortOrder", assignment.SortOrder);
        command.Parameters.AddWithValue("$isEnabled", assignment.IsEnabled ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return assignment;
    }

    public async Task<IReadOnlyList<RPThemeProfileThemeAssignment>> ListProfileAssignmentsAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var assignments = new List<RPThemeProfileThemeAssignment>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ProfileId, ThemeId, Tier, Weight, SortOrder, IsEnabled FROM RPThemeProfileThemeAssignments WHERE ProfileId = $profileId ORDER BY SortOrder, Id";
        command.Parameters.AddWithValue("$profileId", profileId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            assignments.Add(new RPThemeProfileThemeAssignment
            {
                Id = reader.GetString(0),
                ProfileId = reader.GetString(1),
                ThemeId = reader.GetString(2),
                Tier = Enum.TryParse<RPThemeTier>(reader.GetString(3), out var tier) ? tier : RPThemeTier.Neutral,
                Weight = reader.GetDecimal(4),
                SortOrder = reader.GetInt32(5),
                IsEnabled = reader.GetInt32(6) == 1
            });
        }

        return assignments;
    }

    private static decimal GetDefaultWeightForTier(RPThemeTier tier)
        => tier switch
        {
            RPThemeTier.MustHave => 1.0m,
            RPThemeTier.StronglyPrefer => 0.8m,
            RPThemeTier.NiceToHave => 0.6m,
            RPThemeTier.Neutral => 0.5m,
            RPThemeTier.Discouraged => 0.2m,
            RPThemeTier.HardDealBreaker => 0m,
            _ => 0.5m
        };

    public async Task<bool> DeleteProfileAssignmentAsync(string assignmentId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RPThemeProfileThemeAssignments WHERE Id = $id";
        command.Parameters.AddWithValue("$id", assignmentId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlyList<RPThemeImportResult>> ImportFromMarkdownAsync(
        IReadOnlyList<RPThemeImportFile> files,
        CancellationToken cancellationToken = default)
    {
        if (files.Count == 0)
        {
            return [];
        }

        var runId = Guid.NewGuid().ToString("N");
        var startedUtc = DateTime.UtcNow;
        var results = new List<RPThemeImportResult>();

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureGlobalThemeLibraryProfileAsync(connection, cancellationToken);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await EnsureImportRunPlaceholderAsync(connection, tx, runId, startedUtc, cancellationToken);

            foreach (var file in files)
            {
                var warnings = new List<string>();
                try
                {
                    var parsed = ParseMarkdown(file.MarkdownContent, file.SourcePath, warnings);
                    var theme = new RPTheme
                    {
                        Id = parsed.Id,
                        ParentThemeId = parsed.ParentThemeId,
                        Label = parsed.Label,
                        Category = parsed.Category,
                        Weight = parsed.Weight,
                        Description = parsed.Description,
                        IsEnabled = true,
                        Keywords = parsed.Keywords.Select((kw, idx) => new RPThemeKeyword
                        {
                            ThemeId = parsed.Id,
                            GroupName = kw.Group,
                            Keyword = kw.Value,
                            SortOrder = idx
                        }).ToList(),
                        StatAffinities = parsed.StatAffinities.Select(x => new RPThemeStatAffinity
                        {
                            ThemeId = parsed.Id,
                            StatName = x.StatName,
                            Value = x.Value,
                            Rationale = x.Rationale
                        }).ToList(),
                        PhaseGuidance = parsed.PhaseGuidance.Select(x => new RPThemePhaseGuidance
                        {
                            ThemeId = parsed.Id,
                            Phase = x.Phase,
                            GuidanceText = x.Text
                        }).ToList()
                    };

                    await SaveThemeWithConnectionAsync(connection, tx, theme, cancellationToken);
                    await SaveImportIssueBatchAsync(connection, tx, runId, file.SourcePath, "Warning", warnings, cancellationToken);

                    results.Add(new RPThemeImportResult
                    {
                        SourcePath = file.SourcePath,
                        ThemeId = parsed.Id,
                        Imported = true,
                        Warnings = warnings
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "RP theme import failed for source {SourcePath} in run {RunId}.", file.SourcePath, runId);
                    await SaveImportIssueAsync(connection, tx, runId, file.SourcePath, "Error", ex.Message, cancellationToken);
                    results.Add(new RPThemeImportResult
                    {
                        SourcePath = file.SourcePath,
                        Imported = false,
                        Error = ex.Message,
                        Warnings = warnings
                    });
                }
            }

            await SaveImportRunAsync(connection, tx, runId, startedUtc, results, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RP theme markdown import run {RunId} failed and was rolled back.", runId);
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        return results;
    }

    public async Task<IReadOnlyList<RPThemeImportResult>> SyncFromMarkdownDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = (directoryPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            throw new ArgumentException("A markdown directory path is required.", nameof(directoryPath));
        }

        var resolvedPath = ResolveSyncDirectoryPath(normalizedPath);
        if (resolvedPath is null)
        {
            throw new DirectoryNotFoundException($"Markdown directory not found: {normalizedPath}");
        }

        var files = Directory
            .GetFiles(resolvedPath, "*.md", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(path => new RPThemeImportFile(
                Path.GetRelativePath(resolvedPath, path).Replace('\\', '/'),
                File.ReadAllText(path)))
            .ToList();

        if (files.Count == 0)
        {
            return [];
        }

        _logger.LogInformation("Starting RP theme markdown sync from {DirectoryPath} with {FileCount} file(s).", resolvedPath, files.Count);
        IReadOnlyList<RPThemeImportResult> results;
        try
        {
            results = await ImportFromMarkdownAsync(files, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RP theme markdown sync from {DirectoryPath} failed before completion.", resolvedPath);
            throw;
        }

        _logger.LogInformation(
            "Completed RP theme markdown sync from {DirectoryPath}: imported={ImportedCount}, failed={FailedCount}.",
            resolvedPath,
            results.Count(x => x.Imported),
            results.Count(x => !x.Imported));

        return results;
    }

    private static string? ResolveSyncDirectoryPath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath) && Directory.Exists(configuredPath))
        {
            return configuredPath;
        }

        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.GetFullPath(Path.Combine(current.FullName, configuredPath));
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    public async Task TruncateRolePlayAndScenarioDataAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var statements = new[]
        {
            "DELETE FROM RolePlayDebugEvents",
            "DELETE FROM RolePlayV2AdaptiveStates",
            "DELETE FROM RolePlayV2CandidateEvaluations",
            "DELETE FROM RolePlayV2PhaseTransitions",
            "DELETE FROM RolePlayV2CompletionMetadata",
            "DELETE FROM RolePlayV2DecisionOptions",
            "DELETE FROM RolePlayV2DecisionPoints",
            "DELETE FROM RolePlayV2ConceptInjections",
            "DELETE FROM RolePlayV2FormulaVersionRefs",
            "DELETE FROM RolePlayV2UnsupportedSessionErrors",
            "DELETE FROM Scenarios",
            "DELETE FROM Sessions WHERE SessionType = 'roleplay'"
        };

        foreach (var sql in statements)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        _logger.LogWarning("Development truncation completed for RP + Scenario runtime data.");
    }

    private static async Task SaveThemeWithConnectionAsync(SqliteConnection connection, SqliteTransaction tx, RPTheme theme, CancellationToken cancellationToken)
    {
        theme.UpdatedUtc = DateTime.UtcNow;
        if (theme.CreatedUtc == default)
        {
            theme.CreatedUtc = theme.UpdatedUtc;
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = tx;
            var hasLegacyProfileIdColumn = false;
            await using (var schemaCommand = connection.CreateCommand())
            {
                schemaCommand.CommandText = "PRAGMA table_info('RPThemes');";
                await using var schemaReader = await schemaCommand.ExecuteReaderAsync(cancellationToken);
                while (await schemaReader.ReadAsync(cancellationToken))
                {
                    if (string.Equals(schemaReader.GetString(1), "ProfileId", StringComparison.OrdinalIgnoreCase))
                    {
                        hasLegacyProfileIdColumn = true;
                        break;
                    }
                }
            }
            command.CommandText = hasLegacyProfileIdColumn
                ? """
                  INSERT INTO RPThemes (Id, ProfileId, Label, Description, Category, Weight, IsEnabled, CreatedUtc, UpdatedUtc)
                  VALUES ($id, $profileId, $label, $description, $category, $weight, $isEnabled, $createdUtc, $updatedUtc)
                  ON CONFLICT(Id) DO UPDATE SET
                      ProfileId = excluded.ProfileId,
                      Label = excluded.Label,
                      Description = excluded.Description,
                      Category = excluded.Category,
                      Weight = excluded.Weight,
                      IsEnabled = excluded.IsEnabled,
                      UpdatedUtc = excluded.UpdatedUtc;
                  """
                : """
                  INSERT INTO RPThemes (Id, Label, Description, Category, Weight, IsEnabled, CreatedUtc, UpdatedUtc)
                  VALUES ($id, $label, $description, $category, $weight, $isEnabled, $createdUtc, $updatedUtc)
                  ON CONFLICT(Id) DO UPDATE SET
                      Label = excluded.Label,
                      Description = excluded.Description,
                      Category = excluded.Category,
                      Weight = excluded.Weight,
                      IsEnabled = excluded.IsEnabled,
                      UpdatedUtc = excluded.UpdatedUtc;
                  """;
            command.Parameters.AddWithValue("$id", theme.Id);
            if (hasLegacyProfileIdColumn)
            {
                command.Parameters.AddWithValue("$profileId", IRPThemeService.GlobalThemeLibraryProfileId);
            }
            command.Parameters.AddWithValue("$label", theme.Label);
            command.Parameters.AddWithValue("$description", theme.Description);
            command.Parameters.AddWithValue("$category", theme.Category);
            command.Parameters.AddWithValue("$weight", theme.Weight);
            command.Parameters.AddWithValue("$isEnabled", theme.IsEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$createdUtc", theme.CreatedUtc.ToString("O"));
            command.Parameters.AddWithValue("$updatedUtc", theme.UpdatedUtc.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await ReplaceThemeChildrenAsync(connection, tx, theme, cancellationToken);

        await using (var deleteHierarchy = connection.CreateCommand())
        {
            deleteHierarchy.Transaction = tx;
            deleteHierarchy.CommandText = "DELETE FROM RPThemeRelationships WHERE ChildThemeId = $themeId";
            deleteHierarchy.Parameters.AddWithValue("$themeId", theme.Id);
            await deleteHierarchy.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(theme.ParentThemeId))
        {
            await using var insertHierarchy = connection.CreateCommand();
            insertHierarchy.Transaction = tx;
            insertHierarchy.CommandText = "INSERT INTO RPThemeRelationships (ParentThemeId, ChildThemeId, SortOrder) VALUES ($parent, $child, 0)";
            insertHierarchy.Parameters.AddWithValue("$parent", theme.ParentThemeId);
            insertHierarchy.Parameters.AddWithValue("$child", theme.Id);
            await insertHierarchy.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ReplaceThemeChildrenAsync(SqliteConnection connection, SqliteTransaction tx, RPTheme theme, CancellationToken cancellationToken)
    {
        var clearTables = new[]
        {
            "DELETE FROM RPThemeKeywords WHERE ThemeId = $themeId",
            "DELETE FROM RPThemeStatAffinities WHERE ThemeId = $themeId",
            "DELETE FROM RPThemePhaseGuidance WHERE ThemeId = $themeId",
            "DELETE FROM RPThemeGuidancePoints WHERE ThemeId = $themeId"
        };

        foreach (var clearSql in clearTables)
        {
            await using var clear = connection.CreateCommand();
            clear.Transaction = tx;
            clear.CommandText = clearSql;
            clear.Parameters.AddWithValue("$themeId", theme.Id);
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var keyword in theme.Keywords)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO RPThemeKeywords (Id, ThemeId, GroupName, Keyword, SortOrder) VALUES ($id, $themeId, $groupName, $keyword, $sortOrder)";
            cmd.Parameters.AddWithValue("$id", string.IsNullOrWhiteSpace(keyword.Id) ? Guid.NewGuid().ToString("N") : keyword.Id);
            cmd.Parameters.AddWithValue("$themeId", theme.Id);
            cmd.Parameters.AddWithValue("$groupName", keyword.GroupName ?? string.Empty);
            cmd.Parameters.AddWithValue("$keyword", keyword.Keyword);
            cmd.Parameters.AddWithValue("$sortOrder", keyword.SortOrder);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var affinity in theme.StatAffinities)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO RPThemeStatAffinities (Id, ThemeId, StatName, Value, Rationale) VALUES ($id, $themeId, $statName, $value, $rationale)";
            cmd.Parameters.AddWithValue("$id", string.IsNullOrWhiteSpace(affinity.Id) ? Guid.NewGuid().ToString("N") : affinity.Id);
            cmd.Parameters.AddWithValue("$themeId", theme.Id);
            cmd.Parameters.AddWithValue("$statName", affinity.StatName);
            cmd.Parameters.AddWithValue("$value", affinity.Value);
            cmd.Parameters.AddWithValue("$rationale", affinity.Rationale ?? string.Empty);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var guidance in theme.PhaseGuidance)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO RPThemePhaseGuidance (Id, ThemeId, Phase, GuidanceText) VALUES ($id, $themeId, $phase, $guidanceText)";
            cmd.Parameters.AddWithValue("$id", string.IsNullOrWhiteSpace(guidance.Id) ? Guid.NewGuid().ToString("N") : guidance.Id);
            cmd.Parameters.AddWithValue("$themeId", theme.Id);
            cmd.Parameters.AddWithValue("$phase", guidance.Phase.ToString());
            cmd.Parameters.AddWithValue("$guidanceText", guidance.GuidanceText);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var point in theme.GuidancePoints)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO RPThemeGuidancePoints (Id, ThemeId, Phase, PointType, Text, SortOrder) VALUES ($id, $themeId, $phase, $pointType, $text, $sortOrder)";
            cmd.Parameters.AddWithValue("$id", string.IsNullOrWhiteSpace(point.Id) ? Guid.NewGuid().ToString("N") : point.Id);
            cmd.Parameters.AddWithValue("$themeId", theme.Id);
            cmd.Parameters.AddWithValue("$phase", point.Phase.ToString());
            cmd.Parameters.AddWithValue("$pointType", point.PointType.ToString());
            cmd.Parameters.AddWithValue("$text", point.Text);
            cmd.Parameters.AddWithValue("$sortOrder", point.SortOrder);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static ParsedThemeDefinition ParseMarkdown(string markdown, string sourcePath, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            throw new InvalidOperationException("Markdown content is empty.");
        }

        var id = MatchValue(markdown, IdPattern()) ?? ToKebabCase(Path.GetFileNameWithoutExtension(sourcePath));
        var label = MatchValue(markdown, LabelPattern()) ?? Path.GetFileNameWithoutExtension(sourcePath);
        var category = MatchValue(markdown, CategoryPattern()) ?? "Uncategorized";
        var description = ExtractDescription(markdown);
        var parentThemeId = MatchValue(markdown, VariantPattern());

        var weightText = MatchValue(markdown, WeightPattern());
        var weight = int.TryParse(weightText, out var parsedWeight) ? Math.Clamp(parsedWeight, 1, 10) : 1;
        if (weightText is null)
        {
            warnings.Add("Missing Weight metadata; defaulted to 1.");
        }

        var keywords = ExtractKeywords(markdown);
        if (keywords.Count == 0)
        {
            warnings.Add("No keywords were detected in Keywords section.");
        }

        var statAffinities = ExtractStatAffinities(markdown);
        if (statAffinities.Count == 0)
        {
            warnings.Add("No stat affinities detected in Stat Affinities section.");
        }

        var phaseGuidance = ExtractPhaseGuidance(markdown);
        if (phaseGuidance.Count == 0)
        {
            warnings.Add("No phase guidance sections detected.");
        }

        return new ParsedThemeDefinition(id, label, category, description, weight, parentThemeId, keywords, statAffinities, phaseGuidance);
    }

    private static string? MatchValue(string content, Regex regex)
    {
        var match = regex.Match(content);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["value"].Value.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string ToKebabCase(string input)
    {
        var normalized = Regex.Replace(input.Trim(), "[^A-Za-z0-9]+", "-");
        normalized = Regex.Replace(normalized, "-+", "-").Trim('-');
        return normalized.ToLowerInvariant();
    }

    private static string ExtractDescription(string markdown)
    {
        var start = markdown.IndexOf("## Description", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return string.Empty;
        }

        var nextHeader = markdown.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var block = nextHeader > start
            ? markdown[start..nextHeader]
            : markdown[start..];

        var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !x.StartsWith("##", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("**", StringComparison.Ordinal))
            .Take(3)
            .ToList();

        return string.Join(' ', lines);
    }

    private static List<(string Group, string Value)> ExtractKeywords(string markdown)
    {
        var keywords = new List<(string Group, string Value)>();
        var start = markdown.IndexOf("## Keywords", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return keywords;
        }

        var nextHeader = markdown.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var block = nextHeader > start
            ? markdown[start..nextHeader]
            : markdown[start..];

        var currentGroup = "General";
        foreach (var raw in block.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("**") && line.EndsWith(":**", StringComparison.Ordinal))
            {
                currentGroup = line.Trim('*', ':', ' ');
                continue;
            }

            if (!line.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            var values = line.TrimStart('-', ' ')
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var value in values)
            {
                keywords.Add((currentGroup, value.Trim()));
            }
        }

        return keywords;
    }

    private static List<(string StatName, int Value, string Rationale)> ExtractStatAffinities(string markdown)
    {
        var affinities = new List<(string StatName, int Value, string Rationale)>();
        var start = markdown.IndexOf("## Stat Affinities", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return affinities;
        }

        var nextHeader = markdown.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var block = nextHeader > start
            ? markdown[start..nextHeader]
            : markdown[start..];

        foreach (var raw in block.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("|", StringComparison.Ordinal) || !line.EndsWith("|", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.Contains("Stat", StringComparison.OrdinalIgnoreCase) || line.Contains("---", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line.Trim('|')
                .Split('|', StringSplitOptions.None)
                .Select(x => x.Trim())
                .ToList();

            if (cells.Count < 3)
            {
                continue;
            }

            var statName = cells[0].Trim('*', ' ');
            var valueText = cells[1].Replace("+", string.Empty, StringComparison.Ordinal).Trim();
            if (string.IsNullOrWhiteSpace(statName) || !int.TryParse(valueText, out var value))
            {
                continue;
            }

            affinities.Add((statName, Math.Clamp(value, -5, 5), cells[2]));
        }

        return affinities;
    }

    private static List<(NarrativePhase Phase, string Text)> ExtractPhaseGuidance(string markdown)
    {
        var list = new List<(NarrativePhase Phase, string Text)>();
        var phaseMap = new (string Header, NarrativePhase Phase)[]
        {
            ("### Build-Up Phase", NarrativePhase.BuildUp),
            ("### Committed Phase", NarrativePhase.Committed),
            ("### Approaching Phase", NarrativePhase.Approaching),
            ("### Climax Phase", NarrativePhase.Climax),
            ("### Reset Phase", NarrativePhase.Reset)
        };

        foreach (var (header, phase) in phaseMap)
        {
            var start = markdown.IndexOf(header, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                continue;
            }

            var nextPhase = markdown.IndexOf("\n### ", start + 1, StringComparison.Ordinal);
            var nextHeader2 = markdown.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
            var end = int.MaxValue;
            if (nextPhase > start)
            {
                end = Math.Min(end, nextPhase);
            }

            if (nextHeader2 > start)
            {
                end = Math.Min(end, nextHeader2);
            }

            var block = end != int.MaxValue
                ? markdown[start..end]
                : markdown[start..];

            var text = string.Join(' ', block.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !x.StartsWith("###", StringComparison.Ordinal))
                .Take(4));

            if (!string.IsNullOrWhiteSpace(text))
            {
                list.Add((phase, text));
            }
        }

        return list;
    }

    private static async Task SaveImportRunAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        string runId,
        DateTime startedUtc,
        IReadOnlyList<RPThemeImportResult> results,
        CancellationToken cancellationToken)
    {
        var warningCount = results.Sum(x => x.Warnings.Count);
        var errorCount = results.Count(x => !x.Imported);

        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO RPThemeImportRuns (Id, StartedUtc, CompletedUtc, ImportedCount, WarningCount, ErrorCount)
            VALUES ($id, $startedUtc, $completedUtc, $importedCount, $warningCount, $errorCount)
            ON CONFLICT(Id) DO UPDATE SET
                CompletedUtc = excluded.CompletedUtc,
                ImportedCount = excluded.ImportedCount,
                WarningCount = excluded.WarningCount,
                ErrorCount = excluded.ErrorCount;
            """;
        command.Parameters.AddWithValue("$id", runId);
        command.Parameters.AddWithValue("$startedUtc", startedUtc.ToString("O"));
        command.Parameters.AddWithValue("$completedUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$importedCount", results.Count(x => x.Imported));
        command.Parameters.AddWithValue("$warningCount", warningCount);
        command.Parameters.AddWithValue("$errorCount", errorCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureImportRunPlaceholderAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        string runId,
        DateTime startedUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO RPThemeImportRuns (Id, StartedUtc, CompletedUtc, ImportedCount, WarningCount, ErrorCount)
            VALUES ($id, $startedUtc, $completedUtc, 0, 0, 0)
            ON CONFLICT(Id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$id", runId);
        command.Parameters.AddWithValue("$startedUtc", startedUtc.ToString("O"));
        command.Parameters.AddWithValue("$completedUtc", startedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SaveImportIssueBatchAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        string runId,
        string sourcePath,
        string severity,
        IReadOnlyList<string> messages,
        CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            await SaveImportIssueAsync(connection, tx, runId, sourcePath, severity, message, cancellationToken);
        }
    }

    private static async Task SaveImportIssueAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        string runId,
        string sourcePath,
        string severity,
        string message,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO RPThemeImportIssues (Id, ImportRunId, SourcePath, Severity, Message)
            VALUES ($id, $importRunId, $sourcePath, $severity, $message);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$importRunId", runId);
        command.Parameters.AddWithValue("$sourcePath", sourcePath);
        command.Parameters.AddWithValue("$severity", severity);
        command.Parameters.AddWithValue("$message", message);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string?> LoadParentThemeIdAsync(SqliteConnection connection, string themeId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ParentThemeId FROM RPThemeRelationships WHERE ChildThemeId = $childThemeId ORDER BY SortOrder LIMIT 1";
        command.Parameters.AddWithValue("$childThemeId", themeId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value?.ToString();
    }

    private static async Task<List<RPThemeKeyword>> LoadThemeKeywordsAsync(SqliteConnection connection, string themeId, CancellationToken cancellationToken)
    {
        var list = new List<RPThemeKeyword>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ThemeId, GroupName, Keyword, SortOrder FROM RPThemeKeywords WHERE ThemeId = $themeId ORDER BY SortOrder, Id";
        command.Parameters.AddWithValue("$themeId", themeId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new RPThemeKeyword
            {
                Id = reader.GetString(0),
                ThemeId = reader.GetString(1),
                GroupName = reader.GetString(2),
                Keyword = reader.GetString(3),
                SortOrder = reader.GetInt32(4)
            });
        }

        return list;
    }

    private static async Task<List<RPThemeStatAffinity>> LoadThemeStatAffinitiesAsync(SqliteConnection connection, string themeId, CancellationToken cancellationToken)
    {
        var list = new List<RPThemeStatAffinity>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ThemeId, StatName, Value, Rationale FROM RPThemeStatAffinities WHERE ThemeId = $themeId ORDER BY StatName";
        command.Parameters.AddWithValue("$themeId", themeId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new RPThemeStatAffinity
            {
                Id = reader.GetString(0),
                ThemeId = reader.GetString(1),
                StatName = reader.GetString(2),
                Value = reader.GetInt32(3),
                Rationale = reader.GetString(4)
            });
        }

        return list;
    }

    private static async Task<List<RPThemePhaseGuidance>> LoadThemePhaseGuidanceAsync(SqliteConnection connection, string themeId, CancellationToken cancellationToken)
    {
        var list = new List<RPThemePhaseGuidance>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ThemeId, Phase, GuidanceText FROM RPThemePhaseGuidance WHERE ThemeId = $themeId";
        command.Parameters.AddWithValue("$themeId", themeId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new RPThemePhaseGuidance
            {
                Id = reader.GetString(0),
                ThemeId = reader.GetString(1),
                Phase = Enum.TryParse<NarrativePhase>(reader.GetString(2), out var phase) ? phase : NarrativePhase.BuildUp,
                GuidanceText = reader.GetString(3)
            });
        }

        return list;
    }

    private static async Task<List<RPThemeGuidancePoint>> LoadThemeGuidancePointsAsync(SqliteConnection connection, string themeId, CancellationToken cancellationToken)
    {
        var list = new List<RPThemeGuidancePoint>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ThemeId, Phase, PointType, Text, SortOrder FROM RPThemeGuidancePoints WHERE ThemeId = $themeId ORDER BY SortOrder";
        command.Parameters.AddWithValue("$themeId", themeId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new RPThemeGuidancePoint
            {
                Id = reader.GetString(0),
                ThemeId = reader.GetString(1),
                Phase = Enum.TryParse<NarrativePhase>(reader.GetString(2), out var phase) ? phase : NarrativePhase.BuildUp,
                PointType = Enum.TryParse<RPThemeGuidancePointType>(reader.GetString(3), out var pointType)
                    ? pointType
                    : RPThemeGuidancePointType.Emphasis,
                Text = reader.GetString(4),
                SortOrder = reader.GetInt32(5)
            });
        }

        return list;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task EnsureGlobalThemeLibraryProfileAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RPThemeProfiles (Id, Name, Description, IsDefault, CreatedUtc, UpdatedUtc)
            VALUES ($id, $name, $description, 0, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$id", IRPThemeService.GlobalThemeLibraryProfileId);
        command.Parameters.AddWithValue("$name", "Global Theme Library");
        command.Parameters.AddWithValue("$description", "Shared RP theme definitions used across profiles.");
        command.Parameters.AddWithValue("$createdUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<bool> RPThemesTableHasProfileIdAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (_rpThemesHasProfileIdColumn.HasValue)
        {
            return _rpThemesHasProfileIdColumn.Value;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info('RPThemes');";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var columnName = reader.GetString(1);
            if (string.Equals(columnName, "ProfileId", StringComparison.OrdinalIgnoreCase))
            {
                _rpThemesHasProfileIdColumn = true;
                return true;
            }
        }

        _rpThemesHasProfileIdColumn = false;
        return false;
    }

    [GeneratedRegex(@"\*\*ID:\*\*\s*`?(?<value>[^`\r\n]+)`?", RegexOptions.IgnoreCase)]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"\*\*Label:\*\*\s*(?<value>[^\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex LabelPattern();

    [GeneratedRegex(@"\*\*Category:\*\*\s*(?<value>[^\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CategoryPattern();

    [GeneratedRegex(@"\*\*Weight:\*\*\s*(?<value>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex WeightPattern();

    [GeneratedRegex(@"\*\*Variant of:\*\*\s*`?(?<value>[^`\r\n]+)`?", RegexOptions.IgnoreCase)]
    private static partial Regex VariantPattern();

    private sealed record ParsedThemeDefinition(
        string Id,
        string Label,
        string Category,
        string Description,
        int Weight,
        string? ParentThemeId,
        IReadOnlyList<(string Group, string Value)> Keywords,
        IReadOnlyList<(string StatName, int Value, string Rationale)> StatAffinities,
        IReadOnlyList<(NarrativePhase Phase, string Text)> PhaseGuidance);
}
