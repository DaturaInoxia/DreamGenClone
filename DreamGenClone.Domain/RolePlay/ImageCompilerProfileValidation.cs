using System.Text.Json;
using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// The one validation path for an <see cref="ImageCompilerProfile"/>. Both the repository (on every write) and the
/// seed (on every open) call this, so there is exactly one definition of a valid profile — duplicated validation
/// is how two call sites drift into disagreeing about what is allowed.
///
/// <para>
/// Every rule here fails fast with a message that names the offending value. None of them substitutes a default:
/// a profile with an unknown pose capability, an unbounded budget, or an uncited negative is not "repaired", it is
/// refused, because a substituted value would produce an image nobody asked for and look identical to one that
/// honoured the request.
/// </para>
/// </summary>
public static class ImageCompilerProfileValidation
{
    public static void Validate(ImageCompilerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (string.IsNullOrWhiteSpace(profile.CheckpointIdentifier))
        {
            throw new InvalidOperationException(
                "An image compiler profile must name the checkpoint it describes (RegisteredModel.ModelIdentifier).");
        }

        if (string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            throw new InvalidOperationException(
                $"Image compiler profile '{profile.CheckpointIdentifier}' has no display name.");
        }

        if (profile.Family == SceneImageModelFamily.Unknown || profile.PromptDialect == SceneImagePromptDialect.Unknown)
        {
            throw new InvalidOperationException(
                $"Image compiler profile '{profile.CheckpointIdentifier}' must declare a model family and prompt dialect; "
                + $"it declares family '{profile.Family}' and dialect '{profile.PromptDialect}'.");
        }

        if (!SceneImagePromptMetadata.IsCompatible(profile.Family, profile.PromptDialect))
        {
            throw new InvalidOperationException(
                $"Image compiler profile '{profile.CheckpointIdentifier}' pairs family '{profile.Family}' with dialect "
                + $"'{profile.PromptDialect}', which is not a valid combination. A dialect the family does not read would "
                + "compile prompts in a language its model ignores.");
        }

        if (profile.PoseInText == ImagePoseInText.Unknown)
        {
            throw new InvalidOperationException(
                $"Image compiler profile '{profile.CheckpointIdentifier}' must declare how much of a pose may be described "
                + "in text (Full, SimpleOnly or Forbidden).");
        }

        if (profile.MaxTokens <= 0)
        {
            throw new InvalidOperationException(
                $"Image compiler profile '{profile.CheckpointIdentifier}' must declare a positive token budget; it declares {profile.MaxTokens}.");
        }

        if (profile.MaxChars <= 0)
        {
            throw new InvalidOperationException(
                $"Image compiler profile '{profile.CheckpointIdentifier}' must declare a positive character budget; it declares {profile.MaxChars}.");
        }

        if (profile.MinChars < 0 || profile.MinChars >= profile.MaxChars)
        {
            throw new InvalidOperationException(
                $"Image compiler profile '{profile.CheckpointIdentifier}' has an inconsistent character budget: MinChars "
                + $"{profile.MinChars} must be at least 0 and below MaxChars {profile.MaxChars}.");
        }

        // The negative-prompt rule (B-135 D10): empty by default, and a non-empty value is only legal with the
        // external research that justifies it. This is what stops the recurring "someone added a negative back".
        if (!string.IsNullOrWhiteSpace(profile.Negative) && string.IsNullOrWhiteSpace(profile.NegativeSource))
        {
            throw new InvalidOperationException(
                $"Image compiler profile '{profile.CheckpointIdentifier}' declares a negative prompt with no source. "
                + "A negative is empty for every checkpoint unless external research supports it; record the citation "
                + "in NegativeSource or clear the negative.");
        }

        RequireJsonArray(profile.RequiredComponentsJson, nameof(profile.RequiredComponentsJson), profile.CheckpointIdentifier);
        RequireJsonArray(profile.ForbiddenTokensJson, nameof(profile.ForbiddenTokensJson), profile.CheckpointIdentifier);
        RequireJsonArray(profile.ExamplesJson, nameof(profile.ExamplesJson), profile.CheckpointIdentifier);
        RequireJsonObject(profile.SettingsEnvelopeJson, nameof(profile.SettingsEnvelopeJson), profile.CheckpointIdentifier);
    }

    private static void RequireJsonArray(string json, string field, string checkpoint)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException(
                $"Image compiler profile '{checkpoint}' has an empty {field}; it must be a JSON array (use \"[]\" for none).");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    $"Image compiler profile '{checkpoint}' has a {field} that is not a JSON array.");
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Image compiler profile '{checkpoint}' has a {field} that is not valid JSON: {exception.Message}");
        }
    }

    private static void RequireJsonObject(string json, string field, string checkpoint)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException(
                $"Image compiler profile '{checkpoint}' has an empty {field}; it must be a JSON object (use \"{{}}\" for none).");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"Image compiler profile '{checkpoint}' has a {field} that is not a JSON object.");
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Image compiler profile '{checkpoint}' has a {field} that is not valid JSON: {exception.Message}");
        }
    }
}
