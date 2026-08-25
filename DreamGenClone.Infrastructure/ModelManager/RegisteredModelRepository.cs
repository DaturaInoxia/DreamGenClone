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
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RegisteredModels (Id, ProviderId, ModelIdentifier, DisplayName, IsEnabled, SupportsThinkingControl, CreatedUtc, ContextWindowSize, Quantization, ParameterCount, Notes, ModelKind, ImageSizeSupported, ImageEditorDiffusionModel, ImageEditorTextEncoder, ImageEditorVae, ImageEditorSteps, ImageEditorCfg, ImageEditorSampler, ImageEditorScheduler, ImageEditorDenoise, ImageEditorAuraFlowShift, ImageEditorCfgNormStrength)
            VALUES ($id, $providerId, $identifier, $displayName, $enabled, $supportsThinkingControl, $created, $ctxWindow, $quant, $paramCount, $notes, $modelKind, $imageSizeSupported, $imageEditorDiffusionModel, $imageEditorTextEncoder, $imageEditorVae, $imageEditorSteps, $imageEditorCfg, $imageEditorSampler, $imageEditorScheduler, $imageEditorDenoise, $imageEditorAuraFlowShift, $imageEditorCfgNormStrength)
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
                ImageEditorDiffusionModel = $imageEditorDiffusionModel,
                ImageEditorTextEncoder = $imageEditorTextEncoder,
                ImageEditorVae = $imageEditorVae,
                ImageEditorSteps = $imageEditorSteps,
                ImageEditorCfg = $imageEditorCfg,
                ImageEditorSampler = $imageEditorSampler,
                ImageEditorScheduler = $imageEditorScheduler,
                ImageEditorDenoise = $imageEditorDenoise,
                ImageEditorAuraFlowShift = $imageEditorAuraFlowShift,
                ImageEditorCfgNormStrength = $imageEditorCfgNormStrength
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

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Registered model saved: {ModelId} ({DisplayName})", model.Id, model.DisplayName);
        return model;
    }

    public async Task<RegisteredModel?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ProviderId, ModelIdentifier, DisplayName, IsEnabled, SupportsThinkingControl, CreatedUtc, ContextWindowSize, Quantization, ParameterCount, Notes, ModelKind, ImageSizeSupported, ImageEditorDiffusionModel, ImageEditorTextEncoder, ImageEditorVae, ImageEditorSteps, ImageEditorCfg, ImageEditorSampler, ImageEditorScheduler, ImageEditorDenoise, ImageEditorAuraFlowShift, ImageEditorCfgNormStrength FROM RegisteredModels WHERE Id = $id";
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
        command.CommandText = "SELECT Id, ProviderId, ModelIdentifier, DisplayName, IsEnabled, SupportsThinkingControl, CreatedUtc, ContextWindowSize, Quantization, ParameterCount, Notes, ModelKind, ImageSizeSupported, ImageEditorDiffusionModel, ImageEditorTextEncoder, ImageEditorVae, ImageEditorSteps, ImageEditorCfg, ImageEditorSampler, ImageEditorScheduler, ImageEditorDenoise, ImageEditorAuraFlowShift, ImageEditorCfgNormStrength FROM RegisteredModels WHERE ProviderId = $providerId ORDER BY DisplayName";
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
                   rm.ContextWindowSize, rm.Quantization, rm.ParameterCount, rm.Notes, rm.ModelKind, rm.ImageSizeSupported,
                   rm.ImageEditorDiffusionModel, rm.ImageEditorTextEncoder, rm.ImageEditorVae, rm.ImageEditorSteps, rm.ImageEditorCfg,
                   rm.ImageEditorSampler, rm.ImageEditorScheduler, rm.ImageEditorDenoise, rm.ImageEditorAuraFlowShift, rm.ImageEditorCfgNormStrength,
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
        ImageEditorDiffusionModel = reader.IsDBNull(13) ? null : reader.GetString(13),
        ImageEditorTextEncoder = reader.IsDBNull(14) ? null : reader.GetString(14),
        ImageEditorVae = reader.IsDBNull(15) ? null : reader.GetString(15),
        ImageEditorSteps = reader.IsDBNull(16) ? null : reader.GetInt32(16),
        ImageEditorCfg = reader.IsDBNull(17) ? null : reader.GetDouble(17),
        ImageEditorSampler = reader.IsDBNull(18) ? null : reader.GetString(18),
        ImageEditorScheduler = reader.IsDBNull(19) ? null : reader.GetString(19),
        ImageEditorDenoise = reader.IsDBNull(20) ? null : reader.GetDouble(20),
        ImageEditorAuraFlowShift = reader.IsDBNull(21) ? null : reader.GetDouble(21),
        ImageEditorCfgNormStrength = reader.IsDBNull(22) ? null : reader.GetDouble(22)
    };
}
