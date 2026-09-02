using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.ModelManager;

public sealed class RegisteredModelRepository : IRegisteredModelRepository
{
    private readonly PersistenceOptions _options;
    private readonly ILogger<RegisteredModelRepository> _logger;

    public RegisteredModelRepository(IOptions<PersistenceOptions> options, ILogger<RegisteredModelRepository> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RegisteredModel> SaveAsync(RegisteredModel model, CancellationToken cancellationToken = default)
    {
        ValidateImagePromptMetadata(model);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RegisteredModels (Id, ProviderId, ModelIdentifier, DisplayName, IsEnabled, SupportsThinkingControl, CreatedUtc, ContextWindowSize, Quantization, ParameterCount, Notes, ModelKind, ImageSizeSupported, SceneImageModelFamily, PromptDialect,
                SupportsImageInput, MaximumInputImages, MaximumInputImageBytes, MaximumInputImagePixels, MaximumInputImageDimension, AcceptedInputMediaTypes, MaximumResponseBytes, RuntimeRevision, ArtifactRevision,
                ImageEditorDiffusionModel, ImageEditorTextEncoder, ImageEditorVae, ImageEditorSteps, ImageEditorCfg, ImageEditorSampler, ImageEditorScheduler, ImageEditorDenoise, ImageEditorAuraFlowShift, ImageEditorCfgNormStrength,
                IdentityMechanism, IdentityStrength, IdentityAdapterRef, IdentityClipVisionRef,
                StructuredOutputMode, MaximumContextTokens, MaximumOutputTokens)
            VALUES ($id, $providerId, $identifier, $displayName, $enabled, $supportsThinkingControl, $created, $ctxWindow, $quant, $paramCount, $notes, $modelKind, $imageSizeSupported, $sceneImageModelFamily, $promptDialect,
                $supportsImageInput, $maximumInputImages, $maximumInputImageBytes, $maximumInputImagePixels, $maximumInputImageDimension, $acceptedInputMediaTypes, $maximumResponseBytes, $runtimeRevision, $artifactRevision,
                $imageEditorDiffusionModel, $imageEditorTextEncoder, $imageEditorVae, $imageEditorSteps, $imageEditorCfg, $imageEditorSampler, $imageEditorScheduler, $imageEditorDenoise, $imageEditorAuraFlowShift, $imageEditorCfgNormStrength,
                $identityMechanism, $identityStrength, $identityAdapterRef, $identityClipVisionRef,
                $structuredOutputMode, $maximumContextTokens, $maximumOutputTokens)
            ON CONFLICT(Id) DO UPDATE SET
                ProviderId = $providerId,
                ModelIdentifier = $identifier,
                DisplayName = $displayName,
                IsEnabled = $enabled,
                SupportsThinkingControl = $supportsThinkingControl,
                ContextWindowSize = $ctxWindow,
                Quantization = $quant,
                ParameterCount = $paramCount,
                Notes = $notes,
                ModelKind = $modelKind,
                ImageSizeSupported = $imageSizeSupported,
                SceneImageModelFamily = $sceneImageModelFamily,
                PromptDialect = $promptDialect,
                SupportsImageInput = $supportsImageInput,
                MaximumInputImages = $maximumInputImages,
                MaximumInputImageBytes = $maximumInputImageBytes,
                MaximumInputImagePixels = $maximumInputImagePixels,
                MaximumInputImageDimension = $maximumInputImageDimension,
                AcceptedInputMediaTypes = $acceptedInputMediaTypes,
                MaximumResponseBytes = $maximumResponseBytes,
                RuntimeRevision = $runtimeRevision,
                ArtifactRevision = $artifactRevision,
                ImageEditorDiffusionModel = $imageEditorDiffusionModel,
                ImageEditorTextEncoder = $imageEditorTextEncoder,
                ImageEditorVae = $imageEditorVae,
                ImageEditorSteps = $imageEditorSteps,
                ImageEditorCfg = $imageEditorCfg,
                ImageEditorSampler = $imageEditorSampler,
                ImageEditorScheduler = $imageEditorScheduler,
                ImageEditorDenoise = $imageEditorDenoise,
                ImageEditorAuraFlowShift = $imageEditorAuraFlowShift,
                ImageEditorCfgNormStrength = $imageEditorCfgNormStrength,
                IdentityMechanism = $identityMechanism,
                IdentityStrength = $identityStrength,
                IdentityAdapterRef = $identityAdapterRef,
                IdentityClipVisionRef = $identityClipVisionRef,
                StructuredOutputMode = $structuredOutputMode,
                MaximumContextTokens = $maximumContextTokens,
                MaximumOutputTokens = $maximumOutputTokens
            """;

        command.Parameters.AddWithValue("$id", model.Id);
        command.Parameters.AddWithValue("$providerId", model.ProviderId);
        command.Parameters.AddWithValue("$identifier", model.ModelIdentifier);
        command.Parameters.AddWithValue("$displayName", model.DisplayName);
        command.Parameters.AddWithValue("$enabled", model.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$supportsThinkingControl", model.SupportsThinkingControl ? 1 : 0);
        command.Parameters.AddWithValue("$created", model.CreatedUtc);
        command.Parameters.AddWithValue("$ctxWindow", model.ContextWindowSize);
        command.Parameters.AddWithValue("$quant", model.Quantization);
        command.Parameters.AddWithValue("$paramCount", model.ParameterCount);
        command.Parameters.AddWithValue("$notes", (object?)model.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$modelKind", (int)model.ModelKind);
        command.Parameters.AddWithValue("$imageSizeSupported", (object?)model.ImageSizeSupported ?? DBNull.Value);
        command.Parameters.AddWithValue("$sceneImageModelFamily", (int)model.SceneImageModelFamily);
        command.Parameters.AddWithValue("$promptDialect", (int)model.PromptDialect);
        command.Parameters.AddWithValue("$supportsImageInput", model.SupportsImageInput ? 1 : 0);
        command.Parameters.AddWithValue("$maximumInputImages", (object?)model.MaximumInputImages ?? DBNull.Value);
        command.Parameters.AddWithValue("$maximumInputImageBytes", (object?)model.MaximumInputImageBytes ?? DBNull.Value);
        command.Parameters.AddWithValue("$maximumInputImagePixels", (object?)model.MaximumInputImagePixels ?? DBNull.Value);
        command.Parameters.AddWithValue("$maximumInputImageDimension", (object?)model.MaximumInputImageDimension ?? DBNull.Value);
        command.Parameters.AddWithValue("$acceptedInputMediaTypes", (object?)model.AcceptedInputMediaTypes ?? DBNull.Value);
        command.Parameters.AddWithValue("$maximumResponseBytes", (object?)model.MaximumResponseBytes ?? DBNull.Value);
        command.Parameters.AddWithValue("$runtimeRevision", (object?)model.RuntimeRevision ?? DBNull.Value);
        command.Parameters.AddWithValue("$artifactRevision", (object?)model.ArtifactRevision ?? DBNull.Value);
        command.Parameters.AddWithValue("$imageEditorDiffusionModel", (object?)model.ImageEditorDiffusionModel ?? DBNull.Value);
        command.Parameters.AddWithValue("$imageEditorTextEncoder", (object?)model.ImageEditorTextEncoder ?? DBNull.Value);
        command.Parameters.AddWithValue("$imageEditorVae", (object?)model.ImageEditorVae ?? DBNull.Value);
        command.Parameters.AddWithValue("$imageEditorSteps", (object?)model.ImageEditorSteps ?? DBNull.Value);
        command.Parameters.AddWithValue("$imageEditorCfg", (object?)model.ImageEditorCfg ?? DBNull.Value);
        command.Parameters.AddWithValue("$imageEditorSampler", (object?)model.ImageEditorSampler ?? DBNull.Value);
        command.Parameters.AddWithValue("$imageEditorScheduler", (object?)model.ImageEditorScheduler ?? DBNull.Value);
        command.Parameters.AddWithValue("$imageEditorDenoise", (object?)model.ImageEditorDenoise ?? DBNull.Value);
        command.Parameters.AddWithValue("$imageEditorAuraFlowShift", (object?)model.ImageEditorAuraFlowShift ?? DBNull.Value);
        command.Parameters.AddWithValue("$imageEditorCfgNormStrength", (object?)model.ImageEditorCfgNormStrength ?? DBNull.Value);
        command.Parameters.AddWithValue("$identityMechanism", (object?)model.IdentityMechanism ?? DBNull.Value);
        command.Parameters.AddWithValue("$identityStrength", (object?)model.IdentityStrength ?? DBNull.Value);
        command.Parameters.AddWithValue("$identityAdapterRef", (object?)model.IdentityAdapterRef ?? DBNull.Value);
        command.Parameters.AddWithValue("$identityClipVisionRef", (object?)model.IdentityClipVisionRef ?? DBNull.Value);
        command.Parameters.AddWithValue("$structuredOutputMode", (int)model.StructuredOutputMode);
        command.Parameters.AddWithValue("$maximumContextTokens", (object?)model.MaximumContextTokens ?? DBNull.Value);
        command.Parameters.AddWithValue("$maximumOutputTokens", (object?)model.MaximumOutputTokens ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Registered model saved: {ModelId} ({DisplayName})", model.Id, model.DisplayName);
        return model;
    }

    public async Task<RegisteredModel?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = $"{ModelSelectColumns} WHERE rm.Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadModel(reader);
        }

        return null;
    }

    public async Task<List<RegisteredModel>> GetByProviderIdAsync(string providerId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = $"{ModelSelectColumns} WHERE rm.ProviderId = $providerId ORDER BY rm.DisplayName";
        command.Parameters.AddWithValue("$providerId", providerId);

        var models = new List<RegisteredModel>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            models.Add(ReadModel(reader));
        }

        return models;
    }

    public async Task<List<RegisteredModel>> GetAllEnabledAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
                 SELECT rm.Id, rm.ProviderId, rm.ModelIdentifier, rm.DisplayName, rm.IsEnabled, rm.SupportsThinkingControl, rm.CreatedUtc,
                     rm.ContextWindowSize, rm.Quantization, rm.ParameterCount, rm.Notes, rm.ModelKind, rm.ImageSizeSupported, rm.SceneImageModelFamily, rm.PromptDialect,
                     rm.SupportsImageInput, rm.MaximumInputImages, rm.MaximumInputImageBytes, rm.MaximumInputImagePixels, rm.MaximumInputImageDimension,
                     rm.AcceptedInputMediaTypes, rm.MaximumResponseBytes, rm.RuntimeRevision, rm.ArtifactRevision,
                     rm.ImageEditorDiffusionModel, rm.ImageEditorTextEncoder, rm.ImageEditorVae, rm.ImageEditorSteps, rm.ImageEditorCfg,
                     rm.ImageEditorSampler, rm.ImageEditorScheduler, rm.ImageEditorDenoise, rm.ImageEditorAuraFlowShift, rm.ImageEditorCfgNormStrength,
                     rm.IdentityMechanism, rm.IdentityStrength, rm.IdentityAdapterRef, rm.IdentityClipVisionRef,
                                         rm.StructuredOutputMode, rm.MaximumContextTokens, rm.MaximumOutputTokens,
                   p.Name AS ProviderName
            FROM RegisteredModels rm
            INNER JOIN Providers p ON rm.ProviderId = p.Id
            WHERE rm.IsEnabled = 1 AND p.IsEnabled = 1
            ORDER BY p.Name, rm.DisplayName
            """;

        var models = new List<RegisteredModel>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var model = ReadModel(reader);
            var providerNameOrdinal = reader.GetOrdinal("ProviderName");
            if (!reader.IsDBNull(providerNameOrdinal))
                model.ProviderName = reader.GetString(providerNameOrdinal);
            models.Add(model);
        }

        return models;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RegisteredModels WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Registered model deleted: {ModelId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    public async Task<bool> ExistsByProviderAndIdentifierAsync(string providerId, string modelIdentifier, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM RegisteredModels WHERE ProviderId = $providerId AND ModelIdentifier = $identifier";
        command.Parameters.AddWithValue("$providerId", providerId);
        command.Parameters.AddWithValue("$identifier", modelIdentifier);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private const string ModelSelectColumns = """
        SELECT rm.Id, rm.ProviderId, rm.ModelIdentifier, rm.DisplayName, rm.IsEnabled, rm.SupportsThinkingControl, rm.CreatedUtc,
               rm.ContextWindowSize, rm.Quantization, rm.ParameterCount, rm.Notes, rm.ModelKind, rm.ImageSizeSupported, rm.SceneImageModelFamily, rm.PromptDialect,
               rm.SupportsImageInput, rm.MaximumInputImages, rm.MaximumInputImageBytes, rm.MaximumInputImagePixels, rm.MaximumInputImageDimension,
               rm.AcceptedInputMediaTypes, rm.MaximumResponseBytes, rm.RuntimeRevision, rm.ArtifactRevision,
               rm.ImageEditorDiffusionModel, rm.ImageEditorTextEncoder, rm.ImageEditorVae, rm.ImageEditorSteps, rm.ImageEditorCfg,
               rm.ImageEditorSampler, rm.ImageEditorScheduler, rm.ImageEditorDenoise, rm.ImageEditorAuraFlowShift, rm.ImageEditorCfgNormStrength,
               rm.IdentityMechanism, rm.IdentityStrength, rm.IdentityAdapterRef, rm.IdentityClipVisionRef,
               rm.StructuredOutputMode, rm.MaximumContextTokens, rm.MaximumOutputTokens
        FROM RegisteredModels rm
        """;

    private static RegisteredModel ReadModel(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        ProviderId = reader.GetString(1),
        ModelIdentifier = reader.GetString(2),
        DisplayName = reader.GetString(3),
        IsEnabled = reader.GetInt32(4) == 1,
        SupportsThinkingControl = reader.GetInt32(5) == 1,
        CreatedUtc = reader.GetString(6),
        ContextWindowSize = reader.GetInt32(7),
        Quantization = reader.GetString(8),
        ParameterCount = reader.GetString(9),
        Notes = reader.IsDBNull(10) ? null : reader.GetString(10),
        ModelKind = (ModelKind)reader.GetInt32(11),
        ImageSizeSupported = reader.IsDBNull(12) ? null : reader.GetString(12),
        SceneImageModelFamily = (SceneImageModelFamily)reader.GetInt32(13),
        PromptDialect = (SceneImagePromptDialect)reader.GetInt32(14),
        SupportsImageInput = reader.GetInt32(15) == 1,
        MaximumInputImages = reader.IsDBNull(16) ? null : reader.GetInt32(16),
        MaximumInputImageBytes = reader.IsDBNull(17) ? null : reader.GetInt64(17),
        MaximumInputImagePixels = reader.IsDBNull(18) ? null : reader.GetInt64(18),
        MaximumInputImageDimension = reader.IsDBNull(19) ? null : reader.GetInt32(19),
        AcceptedInputMediaTypes = reader.IsDBNull(20) ? null : reader.GetString(20),
        MaximumResponseBytes = reader.IsDBNull(21) ? null : reader.GetInt64(21),
        RuntimeRevision = reader.IsDBNull(22) ? null : reader.GetString(22),
        ArtifactRevision = reader.IsDBNull(23) ? null : reader.GetString(23),
        ImageEditorDiffusionModel = reader.IsDBNull(24) ? null : reader.GetString(24),
        ImageEditorTextEncoder = reader.IsDBNull(25) ? null : reader.GetString(25),
        ImageEditorVae = reader.IsDBNull(26) ? null : reader.GetString(26),
        ImageEditorSteps = reader.IsDBNull(27) ? null : reader.GetInt32(27),
        ImageEditorCfg = reader.IsDBNull(28) ? null : reader.GetDouble(28),
        ImageEditorSampler = reader.IsDBNull(29) ? null : reader.GetString(29),
        ImageEditorScheduler = reader.IsDBNull(30) ? null : reader.GetString(30),
        ImageEditorDenoise = reader.IsDBNull(31) ? null : reader.GetDouble(31),
        ImageEditorAuraFlowShift = reader.IsDBNull(32) ? null : reader.GetDouble(32),
        ImageEditorCfgNormStrength = reader.IsDBNull(33) ? null : reader.GetDouble(33),
        IdentityMechanism = reader.IsDBNull(34) ? null : reader.GetString(34),
        IdentityStrength = reader.IsDBNull(35) ? null : reader.GetDouble(35),
        IdentityAdapterRef = reader.IsDBNull(36) ? null : reader.GetString(36),
        IdentityClipVisionRef = reader.IsDBNull(37) ? null : reader.GetString(37),
        StructuredOutputMode = (StructuredOutputMode)reader.GetInt32(38),
        MaximumContextTokens = reader.IsDBNull(39) ? null : reader.GetInt32(39),
        MaximumOutputTokens = reader.IsDBNull(40) ? null : reader.GetInt32(40)
    };

    private static void ValidateImagePromptMetadata(RegisteredModel model)
    {
        var valid = (model.SceneImageModelFamily, model.PromptDialect) switch
        {
            (SceneImageModelFamily.Unknown, SceneImagePromptDialect.Unknown) => true,
            (SceneImageModelFamily.Pony, SceneImagePromptDialect.PonyV6Tags) => true,
            (SceneImageModelFamily.Sdxl, SceneImagePromptDialect.SdxlNaturalLanguage) => true,
            _ => false
        };

        if (!valid)
        {
            throw new ArgumentException(
                $"Image model family '{model.SceneImageModelFamily}' is incompatible with prompt dialect '{model.PromptDialect}'. " +
                "Configure Pony with Pony V6 Tags, SDXL with SDXL Natural Language, or leave both unconfigured.",
                nameof(model));
        }
    }
}
