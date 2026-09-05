using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

/// <summary>
/// SQLite persistence for the app-wide scene asset library. Self-contained schema creation mirrors
/// the other RP repositories. Assets are free-floating rows (not scoped to a character) so the same
/// library can back identity packs, locations, and wardrobe packs.
/// </summary>
public sealed class SceneAssetRepository : ISceneAssetRepository
{
    private readonly string _connectionString;

    public SceneAssetRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task<SceneAsset?> GetAsync(string assetId, CancellationToken cancellationToken = default)
    {
        Require(assetId, "Asset id");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Kind, Status, Prompt, SourceAssetId, ModelSnapshotJson, FileRelativePath,
                   MediaType, Width, Height, ByteLength, Sha256, FaceView, IdentityPackId, CharacterProfileId,
                     ErrorMessage, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc, Type, AssociationMetadataJson,
                                         SourceApprovalDecisionId, SourceSceneImageId, SourceSha256, SourceProvenanceJson,
                                         ProductionApprovalStatus, ConsentState, LicenseState, LicenseLabel, ApprovedUseScope,
                                         ContentPolicyKey, CompatibilityMetadataJson, ProductionVersion, SupersedesAssetId, ProductionApprovedUtc,
                                         IsContainerOnly
            FROM SceneAssets
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", assetId.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadAsset(reader);
    }

    public async Task<IReadOnlyList<SceneAsset>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Kind, Status, Prompt, SourceAssetId, ModelSnapshotJson, FileRelativePath,
                   MediaType, Width, Height, ByteLength, Sha256, FaceView, IdentityPackId, CharacterProfileId,
                     ErrorMessage, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc, Type, AssociationMetadataJson,
                                         SourceApprovalDecisionId, SourceSceneImageId, SourceSha256, SourceProvenanceJson,
                                         ProductionApprovalStatus, ConsentState, LicenseState, LicenseLabel, ApprovedUseScope,
                                         ContentPolicyKey, CompatibilityMetadataJson, ProductionVersion, SupersedesAssetId, ProductionApprovedUtc,
                                         IsContainerOnly
            FROM SceneAssets
            ORDER BY CreatedUtc DESC;
            """;

        var results = new List<SceneAsset>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadAsset(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<SceneAsset>> ListByPackAsync(
        string identityPackId, CancellationToken cancellationToken = default)
    {
        Require(identityPackId, "Identity pack id");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Kind, Status, Prompt, SourceAssetId, ModelSnapshotJson, FileRelativePath,
                   MediaType, Width, Height, ByteLength, Sha256, FaceView, IdentityPackId, CharacterProfileId,
                     ErrorMessage, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc, Type, AssociationMetadataJson,
                                         SourceApprovalDecisionId, SourceSceneImageId, SourceSha256, SourceProvenanceJson,
                                         ProductionApprovalStatus, ConsentState, LicenseState, LicenseLabel, ApprovedUseScope,
                                         ContentPolicyKey, CompatibilityMetadataJson, ProductionVersion, SupersedesAssetId, ProductionApprovedUtc,
                                         IsContainerOnly
            FROM SceneAssets
            WHERE IdentityPackId = $packId
            ORDER BY CreatedUtc ASC;
            """;
        command.Parameters.AddWithValue("$packId", identityPackId.Trim());

        var results = new List<SceneAsset>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadAsset(reader));
        }

        return results;
    }

    public async Task<SceneAssetImage?> GetImageAsync(
        string imageId, CancellationToken cancellationToken = default)
    {
        Require(imageId, "Image id");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{ImageSelectSql} WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", imageId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadImage(reader) : null;
    }

    public async Task<IReadOnlyList<SceneAssetImage>> ListImagesAsync(
        string assetId, CancellationToken cancellationToken = default)
    {
        Require(assetId, "Asset id");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{ImageSelectSql} WHERE AssetId = $assetId ORDER BY CreatedUtc DESC, Id DESC;";
        command.Parameters.AddWithValue("$assetId", assetId.Trim());
        var images = new List<SceneAssetImage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) images.Add(ReadImage(reader));
        return images;
    }

    public async Task UpsertImageAsync(
        SceneAssetImage image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        Require(image.Id, "Image id");
        Require(image.AssetId, "Asset id");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SceneAssetImages (
                Id, AssetId, Kind, Status, Prompt, SourceImageId, ModelSnapshotJson,
                AssociationMetadataJson, FileRelativePath, MediaType, Width, Height, ByteLength,
                Sha256, ErrorMessage, SourceProvenanceJson, ProductionApprovalStatus, ConsentState,
                LicenseState, LicenseLabel, ApprovedUseScope, ContentPolicyKey,
                CompatibilityMetadataJson, ProductionVersion, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc)
            VALUES (
                $id, $assetId, $kind, $status, $prompt, $sourceImageId, $modelSnapshotJson,
                $associationMetadataJson, $fileRelativePath, $mediaType, $width, $height, $byteLength,
                $sha256, $errorMessage, $sourceProvenanceJson, $productionApprovalStatus, $consentState,
                $licenseState, $licenseLabel, $approvedUseScope, $contentPolicyKey,
                $compatibilityMetadataJson, $productionVersion, $createdUtc, $startedUtc, $completedUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Status = excluded.Status,
                ModelSnapshotJson = excluded.ModelSnapshotJson,
                AssociationMetadataJson = excluded.AssociationMetadataJson,
                FileRelativePath = excluded.FileRelativePath,
                MediaType = excluded.MediaType,
                Width = excluded.Width,
                Height = excluded.Height,
                ByteLength = excluded.ByteLength,
                Sha256 = excluded.Sha256,
                ErrorMessage = excluded.ErrorMessage,
                SourceProvenanceJson = excluded.SourceProvenanceJson,
                ProductionApprovalStatus = excluded.ProductionApprovalStatus,
                ConsentState = excluded.ConsentState,
                LicenseState = excluded.LicenseState,
                LicenseLabel = excluded.LicenseLabel,
                ApprovedUseScope = excluded.ApprovedUseScope,
                ContentPolicyKey = excluded.ContentPolicyKey,
                CompatibilityMetadataJson = excluded.CompatibilityMetadataJson,
                ProductionVersion = excluded.ProductionVersion,
                StartedUtc = excluded.StartedUtc,
                CompletedUtc = excluded.CompletedUtc,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        AddImageParameters(command, image);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SceneAssetImage> ApproveImageForProductionAsync(
        string imageId,
        string sourceProvenanceJson,
        SceneAssetConsentState consentState,
        SceneAssetLicenseState licenseState,
        string licenseLabel,
        SceneAssetApprovedUseScope approvedUseScope,
        string contentPolicyKey,
        string compatibilityMetadataJson,
        CancellationToken cancellationToken = default)
    {
        Require(imageId, "Image id");
        Require(sourceProvenanceJson, "Source provenance");
        Require(licenseLabel, "License label");
        Require(contentPolicyKey, "Content policy key");
        Require(compatibilityMetadataJson, "Compatibility metadata");
        if (consentState == SceneAssetConsentState.Unknown)
            throw new InvalidOperationException("Production image consent must be Confirmed or NotApplicable.");
        if (licenseState == SceneAssetLicenseState.Unknown)
            throw new InvalidOperationException("Production image license state must be Confirmed or NotApplicable.");
        ValidateUseScope(approvedUseScope);

        var image = await GetImageAsync(imageId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene asset image '{imageId}' was not found.");
        if (image.Status != SceneAssetStatus.Complete
            || string.IsNullOrWhiteSpace(image.FileRelativePath)
            || image.ByteLength <= 0
            || !IsSha256(image.Sha256))
            throw new InvalidOperationException("A scene asset image must be complete with stored bytes and a SHA-256 checksum before production approval.");
        if (image.ProductionApprovalStatus is not null and not SceneAssetProductionApprovalStatus.Draft)
            throw new InvalidOperationException($"Scene asset image '{imageId}' is already {image.ProductionApprovalStatus} for production.");

        var approvedUtc = DateTime.UtcNow;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SceneAssetImages
            SET SourceProvenanceJson = $sourceProvenanceJson,
                ProductionApprovalStatus = 'Approved',
                ConsentState = $consentState,
                LicenseState = $licenseState,
                LicenseLabel = $licenseLabel,
                ApprovedUseScope = $approvedUseScope,
                ContentPolicyKey = $contentPolicyKey,
                CompatibilityMetadataJson = $compatibilityMetadataJson,
                ProductionVersion = COALESCE(ProductionVersion, 1),
                UpdatedUtc = $approvedUtc
            WHERE Id = $id AND (ProductionApprovalStatus IS NULL OR ProductionApprovalStatus = 'Draft');
            """;
        command.Parameters.AddWithValue("$sourceProvenanceJson", sourceProvenanceJson.Trim());
        command.Parameters.AddWithValue("$consentState", consentState.ToString());
        command.Parameters.AddWithValue("$licenseState", licenseState.ToString());
        command.Parameters.AddWithValue("$licenseLabel", licenseLabel.Trim());
        command.Parameters.AddWithValue("$approvedUseScope", (int)approvedUseScope);
        command.Parameters.AddWithValue("$contentPolicyKey", contentPolicyKey.Trim());
        command.Parameters.AddWithValue("$compatibilityMetadataJson", compatibilityMetadataJson.Trim());
        command.Parameters.AddWithValue("$approvedUtc", approvedUtc.ToString("O"));
        command.Parameters.AddWithValue("$id", imageId.Trim());
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Scene asset image '{imageId}' changed before production approval completed.");
        return (await GetImageAsync(imageId, cancellationToken))!;
    }

    public async Task UpsertAsync(SceneAsset asset, CancellationToken cancellationToken = default)
    {
        Require(asset.Id, "Asset id");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using (var immutableCheck = connection.CreateCommand())
        {
            immutableCheck.CommandText = "SELECT ProductionApprovalStatus FROM SceneAssets WHERE Id = $id;";
            immutableCheck.Parameters.AddWithValue("$id", asset.Id.Trim());
            var status = await immutableCheck.ExecuteScalarAsync(cancellationToken);
            if (status is string persistedStatus
                && !string.Equals(persistedStatus, SceneAssetProductionApprovalStatus.Draft.ToString(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Scene asset '{asset.Id}' is {persistedStatus} for production and is immutable; create a new asset version instead.");
            }
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO SceneAssets (
                Id, Name, Kind, Status, Prompt, SourceAssetId, ModelSnapshotJson, FileRelativePath,
                MediaType, Width, Height, ByteLength, Sha256, FaceView, IdentityPackId, CharacterProfileId,
                ErrorMessage, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc, Type, AssociationMetadataJson,
                SourceApprovalDecisionId, SourceSceneImageId, SourceSha256, SourceProvenanceJson,
                ProductionApprovalStatus, ConsentState, LicenseState, LicenseLabel, ApprovedUseScope,
                ContentPolicyKey, CompatibilityMetadataJson, ProductionVersion, SupersedesAssetId, ProductionApprovedUtc,
                IsContainerOnly)
            VALUES (
                $id, $name, $kind, $status, $prompt, $sourceAssetId, $modelSnapshotJson, $fileRelativePath,
                $mediaType, $width, $height, $byteLength, $sha256, $faceView, $identityPackId, $characterProfileId,
                $errorMessage, $createdUtc, $startedUtc, $completedUtc, $updatedUtc, $type, $associationMetadataJson,
                $sourceApprovalDecisionId, $sourceSceneImageId, $sourceSha256, $sourceProvenanceJson,
                $productionApprovalStatus, $consentState, $licenseState, $licenseLabel, $approvedUseScope,
                $contentPolicyKey, $compatibilityMetadataJson, $productionVersion, $supersedesAssetId, $productionApprovedUtc,
                $isContainerOnly);
            """;
        command.Parameters.AddWithValue("$id", asset.Id.Trim());
        command.Parameters.AddWithValue("$name", asset.Name ?? string.Empty);
        command.Parameters.AddWithValue("$kind", asset.Kind.ToString());
        command.Parameters.AddWithValue("$status", asset.Status.ToString());
        command.Parameters.AddWithValue("$prompt", asset.Prompt ?? string.Empty);
        command.Parameters.AddWithValue("$sourceAssetId", (object?)asset.SourceAssetId ?? DBNull.Value);
        command.Parameters.AddWithValue("$modelSnapshotJson", (object?)asset.ModelSnapshotJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$fileRelativePath", (object?)asset.FileRelativePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$mediaType", asset.MediaType ?? string.Empty);
        command.Parameters.AddWithValue("$width", (object?)asset.Width ?? DBNull.Value);
        command.Parameters.AddWithValue("$height", (object?)asset.Height ?? DBNull.Value);
        command.Parameters.AddWithValue("$byteLength", asset.ByteLength);
        command.Parameters.AddWithValue("$sha256", asset.Sha256 ?? string.Empty);
        command.Parameters.AddWithValue("$faceView", (object?)asset.FaceView?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$identityPackId", (object?)asset.IdentityPackId ?? DBNull.Value);
        command.Parameters.AddWithValue("$characterProfileId", (object?)asset.CharacterProfileId ?? DBNull.Value);
        command.Parameters.AddWithValue("$errorMessage", (object?)asset.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", asset.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$startedUtc", asset.StartedUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$completedUtc", asset.CompletedUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$updatedUtc", asset.UpdatedUtc.ToString("O"));
        AddPromotionParameters(command, asset);
        AddProductionGovernanceParameters(command, asset);
        command.Parameters.AddWithValue("$isContainerOnly", asset.IsContainerOnly ? 1 : 0);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SceneAsset> ApproveForProductionAsync(
        string assetId,
        string sourceProvenanceJson,
        SceneAssetConsentState consentState,
        SceneAssetLicenseState licenseState,
        string licenseLabel,
        SceneAssetApprovedUseScope approvedUseScope,
        string contentPolicyKey,
        string compatibilityMetadataJson,
        CancellationToken cancellationToken = default)
    {
        Require(assetId, "Asset id");
        Require(sourceProvenanceJson, "Source provenance");
        Require(licenseLabel, "License label");
        Require(contentPolicyKey, "Content policy key");
        Require(compatibilityMetadataJson, "Compatibility metadata");
        if (consentState == SceneAssetConsentState.Unknown)
            throw new InvalidOperationException("Production asset consent must be Confirmed or NotApplicable.");
        if (licenseState == SceneAssetLicenseState.Unknown)
            throw new InvalidOperationException("Production asset license state must be Confirmed or NotApplicable.");
        ValidateUseScope(approvedUseScope);

        var asset = await GetAsync(assetId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene asset '{assetId}' was not found.");
        if (asset.Status != SceneAssetStatus.Complete
            || string.IsNullOrWhiteSpace(asset.FileRelativePath)
            || asset.ByteLength <= 0
            || !IsSha256(asset.Sha256))
        {
            throw new InvalidOperationException(
                $"Scene asset '{assetId}' must be complete with stored bytes and a SHA-256 checksum before production approval.");
        }
        if (asset.ProductionApprovalStatus is not null
            && asset.ProductionApprovalStatus != SceneAssetProductionApprovalStatus.Draft)
        {
            throw new InvalidOperationException(
                $"Scene asset '{assetId}' is already {asset.ProductionApprovalStatus} for production and cannot be approved again.");
        }

        var approvedUtc = DateTime.UtcNow;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SceneAssets
            SET SourceProvenanceJson = $sourceProvenanceJson,
                ProductionApprovalStatus = 'Approved',
                ConsentState = $consentState,
                LicenseState = $licenseState,
                LicenseLabel = $licenseLabel,
                ApprovedUseScope = $approvedUseScope,
                ContentPolicyKey = $contentPolicyKey,
                CompatibilityMetadataJson = $compatibilityMetadataJson,
                ProductionVersion = COALESCE(ProductionVersion, 1),
                ProductionApprovedUtc = $approvedUtc,
                UpdatedUtc = $approvedUtc
            WHERE Id = $id AND (ProductionApprovalStatus IS NULL OR ProductionApprovalStatus = 'Draft');
            """;
        command.Parameters.AddWithValue("$sourceProvenanceJson", sourceProvenanceJson.Trim());
        command.Parameters.AddWithValue("$consentState", consentState.ToString());
        command.Parameters.AddWithValue("$licenseState", licenseState.ToString());
        command.Parameters.AddWithValue("$licenseLabel", licenseLabel.Trim());
        command.Parameters.AddWithValue("$approvedUseScope", (int)approvedUseScope);
        command.Parameters.AddWithValue("$contentPolicyKey", contentPolicyKey.Trim());
        command.Parameters.AddWithValue("$compatibilityMetadataJson", compatibilityMetadataJson.Trim());
        command.Parameters.AddWithValue("$approvedUtc", approvedUtc.ToString("O"));
        command.Parameters.AddWithValue("$id", assetId.Trim());
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Scene asset '{assetId}' changed before production approval completed.");

        return (await GetAsync(assetId, cancellationToken))!;
    }

    public async Task CreatePromotedAsync(SceneAsset asset, CancellationToken cancellationToken = default)
    {
        ValidatePromotedAsset(asset);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SceneAssets (
                Id, Name, Kind, Status, Prompt, FileRelativePath, MediaType, Width, Height, ByteLength,
                Sha256, CharacterProfileId, CreatedUtc, CompletedUtc, UpdatedUtc, Type, AssociationMetadataJson,
                SourceApprovalDecisionId, SourceSceneImageId, SourceSha256, SourceProvenanceJson)
            VALUES ($id, $name, $kind, $status, $prompt, $fileRelativePath, $mediaType, $width, $height, $byteLength,
                $sha256, $characterProfileId, $createdUtc, $completedUtc, $updatedUtc, $type, $associationMetadataJson,
                $sourceApprovalDecisionId, $sourceSceneImageId, $sourceSha256, $sourceProvenanceJson);
            """;
        command.Parameters.AddWithValue("$id", asset.Id.Trim());
        command.Parameters.AddWithValue("$name", asset.Name.Trim());
        command.Parameters.AddWithValue("$kind", asset.Kind.ToString());
        command.Parameters.AddWithValue("$status", asset.Status.ToString());
        command.Parameters.AddWithValue("$prompt", asset.Prompt ?? string.Empty);
        command.Parameters.AddWithValue("$fileRelativePath", asset.FileRelativePath!);
        command.Parameters.AddWithValue("$mediaType", asset.MediaType ?? string.Empty);
        command.Parameters.AddWithValue("$width", (object?)asset.Width ?? DBNull.Value);
        command.Parameters.AddWithValue("$height", (object?)asset.Height ?? DBNull.Value);
        command.Parameters.AddWithValue("$byteLength", asset.ByteLength);
        command.Parameters.AddWithValue("$sha256", asset.Sha256);
        command.Parameters.AddWithValue("$characterProfileId", (object?)asset.CharacterProfileId ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", asset.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$completedUtc", asset.CompletedUtc!.Value.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", asset.UpdatedUtc.ToString("O"));
        AddPromotionParameters(command, asset);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(
                $"Approval decision '{asset.SourceApprovalDecisionId}' has already been promoted as {asset.Type} asset '{asset.Name}'.",
                exception);
        }
    }

    public async Task DeleteAsync(string assetId, CancellationToken cancellationToken = default)
    {
        Require(assetId, "Asset id");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using (var guard = connection.CreateCommand())
        {
            guard.CommandText = "SELECT ProductionApprovalStatus FROM SceneAssets WHERE Id = $id;";
            guard.Parameters.AddWithValue("$id", assetId.Trim());
            var status = await guard.ExecuteScalarAsync(cancellationToken);
            if (status is string persistedStatus
                && !string.Equals(persistedStatus, SceneAssetProductionApprovalStatus.Draft.ToString(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Scene asset '{assetId}' is {persistedStatus} for production and cannot be deleted.");
            }
        }

        foreach (var table in new[]
        {
            "CharacterBodyAssetBindings",
            "CharacterWardrobeAssetBindings",
            "CharacterLoraDatasetMembers"
        })
        {
            if (!await TableExistsAsync(connection, table, cancellationToken)) continue;
            await using var referenceCheck = connection.CreateCommand();
            referenceCheck.CommandText = $"SELECT COUNT(*) FROM {table} WHERE SceneAssetId = $id;";
            referenceCheck.Parameters.AddWithValue("$id", assetId.Trim());
            if (Convert.ToInt32(await referenceCheck.ExecuteScalarAsync(cancellationToken)) > 0)
                throw new InvalidOperationException($"Scene asset '{assetId}' is in use and cannot be deleted.");
        }

        await using (var deleteImages = connection.CreateCommand())
        {
            deleteImages.CommandText = "DELETE FROM SceneAssetImages WHERE AssetId = $id;";
            deleteImages.Parameters.AddWithValue("$id", assetId.Trim());
            await deleteImages.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var deleteAsset = connection.CreateCommand();
        deleteAsset.CommandText = "DELETE FROM SceneAssets WHERE Id = $id;";
        deleteAsset.Parameters.AddWithValue("$id", assetId.Trim());
        await deleteAsset.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> CountByFilePathAsync(string fileRelativePath, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SceneAssetImages WHERE FileRelativePath = $path;";
        command.Parameters.AddWithValue("$path", fileRelativePath);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    // ---------------- Readers ----------------

    private static SceneAsset ReadAsset(SqliteDataReader reader)
    {
        var id = reader.GetString(0);
        return new SceneAsset
        {
            Id = id,
            Name = reader.GetString(1),
            Kind = ParseEnum<SceneAssetKind>(reader.GetString(2), id, "SceneAssets"),
            Status = ParseEnum<SceneAssetStatus>(reader.GetString(3), id, "SceneAssets"),
            Prompt = reader.GetString(4),
            SourceAssetId = reader.IsDBNull(5) ? null : reader.GetString(5),
            ModelSnapshotJson = reader.IsDBNull(6) ? null : reader.GetString(6),
            FileRelativePath = reader.IsDBNull(7) ? null : reader.GetString(7),
            MediaType = reader.GetString(8),
            Width = reader.IsDBNull(9) ? null : reader.GetInt32(9),
            Height = reader.IsDBNull(10) ? null : reader.GetInt32(10),
            ByteLength = reader.GetInt64(11),
            Sha256 = reader.GetString(12),
            FaceView = reader.IsDBNull(13) ? null : ParseEnum<SceneImageReferenceFaceView>(reader.GetString(13), id, "SceneAssets"),
            IdentityPackId = reader.IsDBNull(14) ? null : reader.GetString(14),
            CharacterProfileId = reader.IsDBNull(15) ? null : reader.GetString(15),
            ErrorMessage = reader.IsDBNull(16) ? null : reader.GetString(16),
            CreatedUtc = ParseUtc(reader.GetString(17), id, "CreatedUtc"),
            StartedUtc = reader.IsDBNull(18) ? null : ParseUtc(reader.GetString(18), id, "StartedUtc"),
            CompletedUtc = reader.IsDBNull(19) ? null : ParseUtc(reader.GetString(19), id, "CompletedUtc"),
            UpdatedUtc = ParseUtc(reader.GetString(20), id, "UpdatedUtc")
            ,Type = reader.IsDBNull(21) ? null : ParseEnum<SceneAssetType>(reader.GetString(21), id, "SceneAssets")
            ,AssociationMetadataJson = reader.IsDBNull(22) ? null : reader.GetString(22)
            ,SourceApprovalDecisionId = reader.IsDBNull(23) ? null : reader.GetString(23)
            ,SourceSceneImageId = reader.IsDBNull(24) ? null : reader.GetString(24)
            ,SourceSha256 = reader.IsDBNull(25) ? null : reader.GetString(25)
            ,SourceProvenanceJson = reader.IsDBNull(26) ? null : reader.GetString(26)
            ,ProductionApprovalStatus = reader.IsDBNull(27) ? null : ParseEnum<SceneAssetProductionApprovalStatus>(reader.GetString(27), id, "SceneAssets")
            ,ConsentState = reader.IsDBNull(28) ? null : ParseEnum<SceneAssetConsentState>(reader.GetString(28), id, "SceneAssets")
            ,LicenseState = reader.IsDBNull(29) ? null : ParseEnum<SceneAssetLicenseState>(reader.GetString(29), id, "SceneAssets")
            ,LicenseLabel = reader.IsDBNull(30) ? null : reader.GetString(30)
            ,ApprovedUseScope = reader.IsDBNull(31) ? null : (SceneAssetApprovedUseScope)reader.GetInt32(31)
            ,ContentPolicyKey = reader.IsDBNull(32) ? null : reader.GetString(32)
            ,CompatibilityMetadataJson = reader.IsDBNull(33) ? null : reader.GetString(33)
            ,ProductionVersion = reader.IsDBNull(34) ? null : reader.GetInt32(34)
            ,SupersedesAssetId = reader.IsDBNull(35) ? null : reader.GetString(35)
            ,ProductionApprovedUtc = reader.IsDBNull(36) ? null : ParseUtc(reader.GetString(36), id, "ProductionApprovedUtc")
            ,IsContainerOnly = reader.GetInt32(37) != 0
        };
    }

    private const string ImageSelectSql = """
        SELECT Id, AssetId, Kind, Status, Prompt, SourceImageId, ModelSnapshotJson,
               AssociationMetadataJson, FileRelativePath, MediaType, Width, Height, ByteLength,
               Sha256, ErrorMessage, SourceProvenanceJson, ProductionApprovalStatus, ConsentState,
               LicenseState, LicenseLabel, ApprovedUseScope, ContentPolicyKey,
               CompatibilityMetadataJson, ProductionVersion, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc
        FROM SceneAssetImages
        """;

    private static SceneAssetImage ReadImage(SqliteDataReader reader)
    {
        var id = reader.GetString(0);
        return new SceneAssetImage
        {
            Id = id,
            AssetId = reader.GetString(1),
            Kind = ParseEnum<SceneAssetKind>(reader.GetString(2), id, "SceneAssetImages"),
            Status = ParseEnum<SceneAssetStatus>(reader.GetString(3), id, "SceneAssetImages"),
            Prompt = reader.GetString(4),
            SourceImageId = reader.IsDBNull(5) ? null : reader.GetString(5),
            ModelSnapshotJson = reader.IsDBNull(6) ? null : reader.GetString(6),
            AssociationMetadataJson = reader.IsDBNull(7) ? null : reader.GetString(7),
            FileRelativePath = reader.IsDBNull(8) ? null : reader.GetString(8),
            MediaType = reader.GetString(9),
            Width = reader.IsDBNull(10) ? null : reader.GetInt32(10),
            Height = reader.IsDBNull(11) ? null : reader.GetInt32(11),
            ByteLength = reader.GetInt64(12),
            Sha256 = reader.GetString(13),
            ErrorMessage = reader.IsDBNull(14) ? null : reader.GetString(14),
            SourceProvenanceJson = reader.IsDBNull(15) ? null : reader.GetString(15),
            ProductionApprovalStatus = reader.IsDBNull(16) ? null : ParseEnum<SceneAssetProductionApprovalStatus>(reader.GetString(16), id, "SceneAssetImages"),
            ConsentState = reader.IsDBNull(17) ? null : ParseEnum<SceneAssetConsentState>(reader.GetString(17), id, "SceneAssetImages"),
            LicenseState = reader.IsDBNull(18) ? null : ParseEnum<SceneAssetLicenseState>(reader.GetString(18), id, "SceneAssetImages"),
            LicenseLabel = reader.IsDBNull(19) ? null : reader.GetString(19),
            ApprovedUseScope = reader.IsDBNull(20) ? null : (SceneAssetApprovedUseScope)reader.GetInt32(20),
            ContentPolicyKey = reader.IsDBNull(21) ? null : reader.GetString(21),
            CompatibilityMetadataJson = reader.IsDBNull(22) ? null : reader.GetString(22),
            ProductionVersion = reader.IsDBNull(23) ? null : reader.GetInt32(23),
            CreatedUtc = ParseUtc(reader.GetString(24), id, "CreatedUtc"),
            StartedUtc = reader.IsDBNull(25) ? null : ParseUtc(reader.GetString(25), id, "StartedUtc"),
            CompletedUtc = reader.IsDBNull(26) ? null : ParseUtc(reader.GetString(26), id, "CompletedUtc"),
            UpdatedUtc = ParseUtc(reader.GetString(27), id, "UpdatedUtc")
        };
    }

    private static void AddImageParameters(SqliteCommand command, SceneAssetImage image)
    {
        command.Parameters.AddWithValue("$id", image.Id.Trim());
        command.Parameters.AddWithValue("$assetId", image.AssetId.Trim());
        command.Parameters.AddWithValue("$kind", image.Kind.ToString());
        command.Parameters.AddWithValue("$status", image.Status.ToString());
        command.Parameters.AddWithValue("$prompt", image.Prompt ?? string.Empty);
        command.Parameters.AddWithValue("$sourceImageId", (object?)image.SourceImageId ?? DBNull.Value);
        command.Parameters.AddWithValue("$modelSnapshotJson", (object?)image.ModelSnapshotJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$associationMetadataJson", (object?)image.AssociationMetadataJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$fileRelativePath", (object?)image.FileRelativePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$mediaType", image.MediaType ?? string.Empty);
        command.Parameters.AddWithValue("$width", (object?)image.Width ?? DBNull.Value);
        command.Parameters.AddWithValue("$height", (object?)image.Height ?? DBNull.Value);
        command.Parameters.AddWithValue("$byteLength", image.ByteLength);
        command.Parameters.AddWithValue("$sha256", image.Sha256 ?? string.Empty);
        command.Parameters.AddWithValue("$errorMessage", (object?)image.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceProvenanceJson", (object?)image.SourceProvenanceJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$productionApprovalStatus", (object?)image.ProductionApprovalStatus?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$consentState", (object?)image.ConsentState?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$licenseState", (object?)image.LicenseState?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$licenseLabel", (object?)image.LicenseLabel ?? DBNull.Value);
        command.Parameters.AddWithValue("$approvedUseScope", image.ApprovedUseScope is null ? DBNull.Value : (int)image.ApprovedUseScope.Value);
        command.Parameters.AddWithValue("$contentPolicyKey", (object?)image.ContentPolicyKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$compatibilityMetadataJson", (object?)image.CompatibilityMetadataJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$productionVersion", (object?)image.ProductionVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", image.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$startedUtc", image.StartedUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$completedUtc", image.CompletedUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$updatedUtc", image.UpdatedUtc.ToString("O"));
    }

    private static void AddPromotionParameters(SqliteCommand command, SceneAsset asset)
    {
        command.Parameters.AddWithValue("$type", (object?)asset.Type?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$associationMetadataJson", (object?)asset.AssociationMetadataJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceApprovalDecisionId", (object?)asset.SourceApprovalDecisionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceSceneImageId", (object?)asset.SourceSceneImageId ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceSha256", (object?)asset.SourceSha256 ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceProvenanceJson", (object?)asset.SourceProvenanceJson ?? DBNull.Value);
    }

    private static void AddProductionGovernanceParameters(SqliteCommand command, SceneAsset asset)
    {
        command.Parameters.AddWithValue("$productionApprovalStatus", (object?)asset.ProductionApprovalStatus?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$consentState", (object?)asset.ConsentState?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$licenseState", (object?)asset.LicenseState?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$licenseLabel", (object?)asset.LicenseLabel ?? DBNull.Value);
        command.Parameters.AddWithValue("$approvedUseScope", asset.ApprovedUseScope is null ? DBNull.Value : (int)asset.ApprovedUseScope.Value);
        command.Parameters.AddWithValue("$contentPolicyKey", (object?)asset.ContentPolicyKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$compatibilityMetadataJson", (object?)asset.CompatibilityMetadataJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$productionVersion", (object?)asset.ProductionVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$supersedesAssetId", (object?)asset.SupersedesAssetId ?? DBNull.Value);
        command.Parameters.AddWithValue("$productionApprovedUtc", asset.ProductionApprovedUtc?.ToString("O") ?? (object)DBNull.Value);
    }

    private static void ValidateUseScope(SceneAssetApprovedUseScope useScope)
    {
        const SceneAssetApprovedUseScope allScopes =
            SceneAssetApprovedUseScope.CharacterIdentity
            | SceneAssetApprovedUseScope.CharacterBody
            | SceneAssetApprovedUseScope.CharacterWardrobe
            | SceneAssetApprovedUseScope.Location
            | SceneAssetApprovedUseScope.Control
            | SceneAssetApprovedUseScope.ProductionSource
            | SceneAssetApprovedUseScope.CharacterLoraTraining;
        if (useScope == 0 || (useScope & ~allScopes) != 0)
            throw new InvalidOperationException("At least one valid production asset use scope is required.");
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character => char.IsAsciiHexDigit(character));

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static void ValidatePromotedAsset(SceneAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Require(asset.Id, "Asset id");
        Require(asset.Name, "Asset name");
        Require(asset.FileRelativePath, "Promoted asset file path");
        Require(asset.Sha256, "Promoted asset SHA-256");
        Require(asset.SourceApprovalDecisionId, "Source approval decision id");
        Require(asset.SourceSceneImageId, "Source scene image id");
        Require(asset.SourceSha256, "Source SHA-256");
        Require(asset.SourceProvenanceJson, "Source provenance");
        if (asset.Status != SceneAssetStatus.Complete || asset.CompletedUtc is null)
            throw new InvalidOperationException("A promoted scene asset must be complete and finalized.");
        if (asset.Type is null || !Enum.IsDefined(asset.Type.Value))
            throw new InvalidOperationException("A promoted scene asset type is required.");
        if (!string.Equals(asset.Sha256, asset.SourceSha256, StringComparison.Ordinal))
            throw new InvalidOperationException("Promoted asset checksum must exactly match its source checksum.");
    }

    private static TEnum ParseEnum<TEnum>(string value, string id, string table) where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"Invalid {typeof(TEnum).Name} value '{value}' for {table} record '{id}'.");
    }

    private static DateTime ParseUtc(string value, string id, string field)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"Invalid UTC value '{value}' for SceneAssets record '{id}' field '{field}'.");
    }

    private static void Require(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{label} is required.");
        }
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS SceneAssets (
                Id                 TEXT PRIMARY KEY,
                Name               TEXT NOT NULL DEFAULT '',
                Kind               TEXT NOT NULL,
                Status             TEXT NOT NULL,
                Prompt             TEXT NOT NULL DEFAULT '',
                SourceAssetId      TEXT NULL,
                ModelSnapshotJson  TEXT NULL,
                FileRelativePath   TEXT NULL,
                MediaType          TEXT NOT NULL DEFAULT '',
                Width              INTEGER NULL,
                Height             INTEGER NULL,
                ByteLength         INTEGER NOT NULL DEFAULT 0,
                Sha256             TEXT NOT NULL DEFAULT '',
                FaceView           TEXT NULL,
                IdentityPackId     TEXT NULL,
                CharacterProfileId TEXT NULL,
                Type               TEXT NULL,
                AssociationMetadataJson TEXT NULL,
                SourceApprovalDecisionId TEXT NULL,
                SourceSceneImageId TEXT NULL,
                SourceSha256       TEXT NULL,
                SourceProvenanceJson TEXT NULL,
                ProductionApprovalStatus TEXT NULL,
                ConsentState       TEXT NULL,
                LicenseState       TEXT NULL,
                LicenseLabel       TEXT NULL,
                ApprovedUseScope   INTEGER NULL,
                ContentPolicyKey   TEXT NULL,
                CompatibilityMetadataJson TEXT NULL,
                ProductionVersion INTEGER NULL,
                SupersedesAssetId  TEXT NULL,
                ProductionApprovedUtc TEXT NULL,
                IsContainerOnly     INTEGER NOT NULL DEFAULT 0,
                ErrorMessage       TEXT NULL,
                CreatedUtc         TEXT NOT NULL,
                StartedUtc         TEXT NULL,
                CompletedUtc       TEXT NULL,
                UpdatedUtc         TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_SceneAssets_IdentityPackId
                ON SceneAssets (IdentityPackId);
            CREATE INDEX IF NOT EXISTS IX_SceneAssets_Status
                ON SceneAssets (Status);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        foreach (var (name, sql) in new[]
        {
            ("Type", "ALTER TABLE SceneAssets ADD COLUMN Type TEXT NULL"),
            ("AssociationMetadataJson", "ALTER TABLE SceneAssets ADD COLUMN AssociationMetadataJson TEXT NULL"),
            ("SourceApprovalDecisionId", "ALTER TABLE SceneAssets ADD COLUMN SourceApprovalDecisionId TEXT NULL"),
            ("SourceSceneImageId", "ALTER TABLE SceneAssets ADD COLUMN SourceSceneImageId TEXT NULL"),
            ("SourceSha256", "ALTER TABLE SceneAssets ADD COLUMN SourceSha256 TEXT NULL"),
            ("SourceProvenanceJson", "ALTER TABLE SceneAssets ADD COLUMN SourceProvenanceJson TEXT NULL"),
            ("ProductionApprovalStatus", "ALTER TABLE SceneAssets ADD COLUMN ProductionApprovalStatus TEXT NULL"),
            ("ConsentState", "ALTER TABLE SceneAssets ADD COLUMN ConsentState TEXT NULL"),
            ("LicenseState", "ALTER TABLE SceneAssets ADD COLUMN LicenseState TEXT NULL"),
            ("LicenseLabel", "ALTER TABLE SceneAssets ADD COLUMN LicenseLabel TEXT NULL"),
            ("ApprovedUseScope", "ALTER TABLE SceneAssets ADD COLUMN ApprovedUseScope INTEGER NULL"),
            ("ContentPolicyKey", "ALTER TABLE SceneAssets ADD COLUMN ContentPolicyKey TEXT NULL"),
            ("CompatibilityMetadataJson", "ALTER TABLE SceneAssets ADD COLUMN CompatibilityMetadataJson TEXT NULL"),
            ("ProductionVersion", "ALTER TABLE SceneAssets ADD COLUMN ProductionVersion INTEGER NULL"),
            ("SupersedesAssetId", "ALTER TABLE SceneAssets ADD COLUMN SupersedesAssetId TEXT NULL"),
            ("ProductionApprovedUtc", "ALTER TABLE SceneAssets ADD COLUMN ProductionApprovedUtc TEXT NULL"),
            ("IsContainerOnly", "ALTER TABLE SceneAssets ADD COLUMN IsContainerOnly INTEGER NOT NULL DEFAULT 0")
        })
        {
            await using var check = connection.CreateCommand();
            check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('SceneAssets') WHERE name = '{name}'";
            if (Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken)) > 0) continue;
            await using var alter = connection.CreateCommand();
            alter.CommandText = sql;
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var unique = connection.CreateCommand();
        unique.CommandText = """
            CREATE UNIQUE INDEX IF NOT EXISTS UX_SceneAssets_Promotion
            ON SceneAssets (SourceApprovalDecisionId, Type, Name COLLATE NOCASE)
            WHERE SourceApprovalDecisionId IS NOT NULL;
            """;
        await unique.ExecuteNonQueryAsync(cancellationToken);

        await using var imageSchema = connection.CreateCommand();
        imageSchema.CommandText = """
            CREATE TABLE IF NOT EXISTS SceneAssetImages (
                Id TEXT PRIMARY KEY,
                AssetId TEXT NOT NULL,
                Kind TEXT NOT NULL,
                Status TEXT NOT NULL,
                Prompt TEXT NOT NULL DEFAULT '',
                SourceImageId TEXT NULL,
                ModelSnapshotJson TEXT NULL,
                AssociationMetadataJson TEXT NULL,
                FileRelativePath TEXT NULL,
                MediaType TEXT NOT NULL DEFAULT '',
                Width INTEGER NULL,
                Height INTEGER NULL,
                ByteLength INTEGER NOT NULL DEFAULT 0,
                Sha256 TEXT NOT NULL DEFAULT '',
                ErrorMessage TEXT NULL,
                SourceProvenanceJson TEXT NULL,
                ProductionApprovalStatus TEXT NULL,
                ConsentState TEXT NULL,
                LicenseState TEXT NULL,
                LicenseLabel TEXT NULL,
                ApprovedUseScope INTEGER NULL,
                ContentPolicyKey TEXT NULL,
                CompatibilityMetadataJson TEXT NULL,
                ProductionVersion INTEGER NULL,
                CreatedUtc TEXT NOT NULL,
                StartedUtc TEXT NULL,
                CompletedUtc TEXT NULL,
                UpdatedUtc TEXT NOT NULL,
                FOREIGN KEY (AssetId) REFERENCES SceneAssets(Id),
                FOREIGN KEY (SourceImageId) REFERENCES SceneAssetImages(Id)
            );
            CREATE INDEX IF NOT EXISTS IX_SceneAssetImages_AssetId_CreatedUtc
                ON SceneAssetImages (AssetId, CreatedUtc DESC);
            CREATE INDEX IF NOT EXISTS IX_SceneAssetImages_Status
                ON SceneAssetImages (Status);
            """;
        await imageSchema.ExecuteNonQueryAsync(cancellationToken);

        await using var imageColumnCheck = connection.CreateCommand();
        imageColumnCheck.CommandText = "SELECT COUNT(*) FROM pragma_table_info('SceneAssetImages') WHERE name = 'ProductionVersion'";
        if (Convert.ToInt32(await imageColumnCheck.ExecuteScalarAsync(cancellationToken)) == 0)
        {
            await using var alterImage = connection.CreateCommand();
            alterImage.CommandText = "ALTER TABLE SceneAssetImages ADD COLUMN ProductionVersion INTEGER NULL";
            await alterImage.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var imageBackfill = connection.CreateCommand();
        imageBackfill.CommandText = """
            INSERT OR IGNORE INTO SceneAssetImages (
                Id, AssetId, Kind, Status, Prompt, SourceImageId, ModelSnapshotJson,
                AssociationMetadataJson, FileRelativePath, MediaType, Width, Height, ByteLength,
                Sha256, ErrorMessage, SourceProvenanceJson, ProductionApprovalStatus, ConsentState,
                LicenseState, LicenseLabel, ApprovedUseScope, ContentPolicyKey,
                CompatibilityMetadataJson, ProductionVersion, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc)
            SELECT Id, Id, Kind, Status, Prompt, SourceAssetId, ModelSnapshotJson,
                AssociationMetadataJson, FileRelativePath, MediaType, Width, Height, ByteLength,
                Sha256, ErrorMessage, SourceProvenanceJson, ProductionApprovalStatus, ConsentState,
                LicenseState, LicenseLabel, ApprovedUseScope, ContentPolicyKey,
                CompatibilityMetadataJson, ProductionVersion, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc
            FROM SceneAssets
            WHERE IsContainerOnly = 0;
            """;
        await imageBackfill.ExecuteNonQueryAsync(cancellationToken);
    }
}
