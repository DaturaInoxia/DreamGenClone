using System.Text.Json;

namespace DreamGenClone.Application.Validation;

public sealed class SessionImportValidator
{
    private const int SupportedSchemaVersion = 1;

    public ValidationResult Validate(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ValidationResult.Fail("Import payload is empty.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("schemaVersion", out var schemaVersionElement) ||
                schemaVersionElement.ValueKind != JsonValueKind.Number)
            {
                return ValidationResult.Fail("Import payload is missing numeric 'schemaVersion'.");
            }

            var schemaVersion = schemaVersionElement.GetInt32();
            if (schemaVersion != SupportedSchemaVersion)
            {
                return ValidationResult.Fail($"Unsupported schemaVersion '{schemaVersion}'. Expected '{SupportedSchemaVersion}'.");
            }

            if (!root.TryGetProperty("sessionType", out var sessionTypeElement) ||
                sessionTypeElement.ValueKind != JsonValueKind.String)
            {
                return ValidationResult.Fail("Import payload is missing string 'sessionType'.");
            }

            if (!root.TryGetProperty("payload", out var payloadElement) ||
                payloadElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return ValidationResult.Fail("Import payload is missing required 'payload'.");
            }

            return ValidationResult.Success();
        }
        catch (JsonException ex)
        {
            return ValidationResult.Fail($"Invalid JSON payload: {ex.Message}");
        }
    }
}

public sealed record ValidationResult(bool IsValid, string? Error)
{
    public static ValidationResult Success() => new(true, null);

    public static ValidationResult Fail(string error) => new(false, error);
}
