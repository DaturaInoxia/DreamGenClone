using DreamGenClone.Domain.RolePlay;
using System.Text.Json;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Shared review-deck projection: a flat, route-agnostic row rendered by the Review Deck and the
/// Compare Deck. All three candidate sources (legacy produced images, production-studio attempts,
/// asset-library candidate batches) map into this one shape.
/// </summary>
public sealed record ReviewDeckItem(
    string Id,
    string? StoragePath,
    string? SessionId,
    string? InteractionId,
    string? AssetId,
    string? ProductionGroupId,
    string? ParentImageId,
    string Kind,
    string Status,
    SceneImageProductionStage? Stage,
    SceneImageStatus? ExecutionStatus,
    string? Prompt,
    string? NegativePrompt,
    string? Model,
    string? Sha256,
    string CreatedUtc,
    string? Details,
    SceneImageAttemptDisposition? Disposition,
    ProducedImageReferenceKind? ReferenceKind,
    ProducedImageVisionSource? VisionSource,
    string? VisionText,
    string? PromptCompiled,
    string? PromptEdited,
    long? Seed,
    string? EndpointId,
    string? IdentityStrategy,
    SceneImageRefusalMode? RefusalMode,
    string? AppliedReferencesJson,
    string? CostJson,
    string? ScoreJson,
    string? CandidateNotes);

/// <summary>Maps each candidate source into <see cref="ReviewDeckItem"/>.</summary>
public static class ReviewDeckMapper
{
    public static ReviewDeckItem MapLegacy(ProducedImage image) => new(
        image.Id,
        image.StoragePath,
        image.SessionId,
        image.InteractionId,
        null,
        null,
        image.ParentImageId,
        image.Kind.ToString(),
        image.Status.ToString(),
        null,
        null,
        image.PromptCompiled ?? image.PromptEdited,
        image.NegativePrompt,
        image.ModelId,
        null,
        image.CreatedUtc,
        null,
        null,
        image.ReferenceKind,
        image.VisionSource,
        image.VisionText,
        image.PromptCompiled,
        image.PromptEdited,
        image.Seed,
        image.EndpointId,
        image.IdentityStrategy,
        image.RefusalMode,
        image.AppliedReferencesJson,
        image.CostJson,
        image.ScoreJson,
        image.CandidateNotes);

    public static ReviewDeckItem MapStudio(SceneImageRecord image, bool isApproved) => new(
        image.Id,
        image.FileRelativePath,
        image.SessionId,
        image.InteractionId,
        null,
        image.ProductionGroupId,
        image.SourceImageId ?? image.RegenerateOfId,
        image.Operation.ToString(),
        isApproved ? "Approved" : image.Disposition?.ToString() ?? "Undecided",
        image.ProductionStage,
        image.Status,
        image.PromptSnapshot,
        image.NegativePromptSnapshot,
        image.ModelIdentifier,
        image.Sha256,
        image.CreatedUtc.ToString("O"),
        image.ErrorMessage,
        image.Disposition,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null);

    public static ReviewDeckItem MapAsset(SceneAssetImage image) => new(
        image.Id,
        image.FileRelativePath,
        null,
        null,
        image.AssetId,
        null,
        image.SourceImageId,
        image.Kind.ToString(),
        image.CandidateDecision?.ToString() ?? "Undecided",
        null,
        null,
        image.Prompt,
        null,
        DescribeModelSnapshot(image.ModelSnapshotJson),
        image.Sha256,
        image.CreatedUtc.ToString("O"),
        image.ErrorMessage,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        image.CandidateNotes);

    /// <summary>
    /// Render a stored model-snapshot JSON blob as the model actually used, for display only.
    /// Non-JSON or unrecognised values are shown verbatim rather than being hidden.
    /// </summary>
    private static string? DescribeModelSnapshot(string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(snapshotJson);
            var root = document.RootElement;
            var identifier = root.TryGetProperty("modelIdentifier", out var model) && model.ValueKind == JsonValueKind.String
                ? model.GetString()
                : null;
            var provider = root.TryGetProperty("providerName", out var providerElement) && providerElement.ValueKind == JsonValueKind.String
                ? providerElement.GetString()
                : null;

            if (!string.IsNullOrWhiteSpace(identifier) && !string.IsNullOrWhiteSpace(provider))
            {
                return $"{identifier} ({provider})";
            }

            return !string.IsNullOrWhiteSpace(identifier)
                ? identifier
                : !string.IsNullOrWhiteSpace(provider) ? provider : snapshotJson;
        }
        catch (JsonException)
        {
            return snapshotJson;
        }
    }
}
