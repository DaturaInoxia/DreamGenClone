using System.Security.Cryptography;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// Asset Manager subjects: the compiled-edit path used by the Asset Studio editor and the Character
/// Identity steps. Owns the write block that used to live inside the asset editing handler.
/// </summary>
public sealed class SceneAssetMediaEditSubjectWriter : IMediaEditSubjectWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISceneAssetRepository _assets;
    private readonly ISceneAssetImageEditRepository _edits;
    private readonly ISceneAssetStorageService _storage;
    private readonly MediaEditReferenceResolver _references;
    private readonly ICharacterImageAssetStorageService? _identityStorage;
    private readonly IImagePresetService? _presets;

    public SceneAssetMediaEditSubjectWriter(
        ISceneAssetRepository assets,
        ISceneAssetImageEditRepository edits,
        ISceneAssetStorageService storage,
        MediaEditReferenceResolver references,
        ICharacterImageAssetStorageService? identityStorage = null,
        IImagePresetService? presets = null)
    {
        _assets = assets;
        _edits = edits;
        _storage = storage;
        _references = references;
        _identityStorage = identityStorage;
        _presets = presets;
    }

    public MediaEditSubjectKind Kind => MediaEditSubjectKind.AssetImage;

    public async Task<MediaEditRunPlan?> PrepareAsync(
        MediaEditRunContext context, CancellationToken cancellationToken = default)
    {
        // An operation is not a render: it needs no editor model, no prompt revision and no references,
        // so it is prepared on its own path instead of being forced through the edit validation below. A
        // masked-region edit - and an outpaint - are EDITs that also carry geometry, so they stay on the
        // compiled-edit path: the row holds the prompt revision the run must resolve, and preparing them
        // here built a plan with no prompt at all (CASE-24: "An edit run requires the compiled prompt its
        // subject writer prepared").
        if (context.Operation.Kind is not MediaEditOperationKind.Edit
            and not MediaEditOperationKind.MaskedRegion
            and not MediaEditOperationKind.Outpaint)
        {
            return await PrepareOperationAsync(context, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(context.ExplicitEditorModelId))
            throw new InvalidOperationException("An exact asset image editor model is required.");

        var image = await _assets.GetImageAsync(context.ImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Asset edit image '{context.ImageId}' was not found.");

        // A redelivered job must not edit an image that already finished.
        if (image.Status == SceneAssetStatus.Complete)
            return null;

        if (image.Kind != SceneAssetKind.Edited
            || string.IsNullOrWhiteSpace(image.SourceImageId)
            || string.IsNullOrWhiteSpace(image.SourceProvenanceJson))
        {
            throw new InvalidOperationException(
                "Asset image editing requires an edited image with source and compiler provenance.");
        }

        // An identity run carries the approved identity-pack faces on the row instead of a compiled prompt
        // revision, so it is resolved from those references and is never validated against a compiler artifact.
        var identityBindings = MediaEditIdentityProvenance.TryRead(image.SourceProvenanceJson);
        if (identityBindings is not null)
            return await PrepareIdentityAsync(image, context, identityBindings, cancellationToken);

        // A preset run is the second authored-instruction kind (B-133): the row carries the preset it was assembled
        // from, so there is no compiler artifact either - and the instruction is re-derived and checksummed rather
        // than trusted.
        var preset = MediaEditPresetProvenance.TryRead(image.SourceProvenanceJson);
        if (preset is not null)
            return await PreparePresetAsync(image, context, preset, cancellationToken);

        // A multi-angle camera run is the third authored-instruction kind: the row carries the pose it was assembled
        // from (the editor LoRA's <sks> grammar), and the instruction is re-derived and checksummed rather than trusted.
        var multiAngle = MediaEditMultiAngleProvenance.TryRead(image.SourceProvenanceJson);
        if (multiAngle is not null)
            return await PrepareMultiAngleAsync(image, context, multiAngle, cancellationToken);

        using var provenance = JsonDocument.Parse(image.SourceProvenanceJson);
        var root = provenance.RootElement;
        var editSessionId = root.GetProperty("editSessionId").GetString()
            ?? throw new InvalidOperationException("Asset edit provenance is missing its edit session id.");
        var attemptId = root.GetProperty("compilationAttemptId").GetString()
            ?? throw new InvalidOperationException("Asset edit provenance is missing its compilation attempt id.");
        var revisionId = root.GetProperty("promptRevisionId").GetString()
            ?? throw new InvalidOperationException("Asset edit provenance is missing its prompt revision id.");
        var sourceSha256 = root.GetProperty("sourceImageSha256").GetString()
            ?? throw new InvalidOperationException("Asset edit provenance is missing the source checksum.");
        var promptSha256 = root.GetProperty("promptSha256").GetString()
            ?? throw new InvalidOperationException("Asset edit provenance is missing the prompt checksum.");

        var session = await _edits.GetSessionAsync(editSessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Asset edit session '{editSessionId}' was not found.");
        if (!string.Equals(session.AssetId, image.AssetId, StringComparison.Ordinal)
            || !string.Equals(session.SourceImageId, image.SourceImageId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Asset edit provenance does not belong to the queued asset image.");
        }

        var revision = await _edits.GetExecutableRevisionAsync(
            session.Id, attemptId, revisionId, sourceSha256, promptSha256, cancellationToken);
        if (!string.Equals(revision.Prompt, image.Prompt, StringComparison.Ordinal))
            throw new InvalidOperationException("The queued asset edit prompt does not match the accepted prompt revision.");

        var source = await _assets.GetImageAsync(image.SourceImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Source scene asset image '{image.SourceImageId}' was not found.");
        if (!string.Equals(source.AssetId, image.AssetId, StringComparison.Ordinal)
            || source.Status != SceneAssetStatus.Complete
            || string.IsNullOrWhiteSpace(source.FileRelativePath))
        {
            throw new InvalidOperationException("The source asset image is not complete, stored, and owned by the queued asset.");
        }

        var sourceFileRelativePath = source.FileRelativePath;

        var applications = string.IsNullOrWhiteSpace(context.ReferenceApplicationsJson)
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<ReferenceApplicationSelection>>(
                context.ReferenceApplicationsJson, JsonOptions)
                ?? throw new InvalidOperationException("Asset edit reference applications are invalid.");
        // The resolver needs the EXACT registered editor model to prove the declared strategy is qualified;
        // handing it nothing made every reference-carrying asset edit fail at run time.
        var references = await _references.ResolveAsync(
            context.ExplicitEditorModelId, applications, ReferenceStrategyCatalogue.ReferenceImageSurface.Edit, cancellationToken);

        // The compiled-edit path's reference list. The role clause is appended later, at send time, from the images
        // actually sent (S3) — a prompt drafted here with no bindings cannot know which of them will survive
        // revalidation.
        return new MediaEditRunPlan(
            image.Id,
            source.Id,
            token => _storage.OpenReadAsync(sourceFileRelativePath, token),
            sourceSha256,
            context.Operation,
            Prompt: revision.Prompt,
            References: references,
            Editor: new MediaEditEditorResolution(context.ExplicitEditorModelId, RequiresAdultContentPolicy: false),
            LogScope: $"AssetId={image.AssetId}");
    }

    /// <summary>
    /// The preset-authored twin of the identity stage: the row's prompt is the instruction a picked preset assembled
    /// (B-133), and this re-derives it from the store to prove it is the same text. Without that check a preset run
    /// would execute whatever string the row happened to hold - and the one thing a queued instruction must never do
    /// is run after the wording it was assembled from has changed underneath it.
    /// </summary>
    private async Task<MediaEditRunPlan> PreparePresetAsync(
        SceneAssetImage image,
        MediaEditRunContext context,
        MediaEditPresetInstruction preset,
        CancellationToken cancellationToken)
    {
        if (_presets is null)
        {
            throw new InvalidOperationException(
                "A preset edit requires the preset resolver, which is not registered in this host.");
        }

        var instruction = await _presets.ResolveInstructionAsync(
            preset.PresetKey, ImagePresetMode.Change, preset.CharacterId, cancellationToken);

        var expected = MediaEditPresetProvenance.InstructionSha256(instruction);
        if (!string.Equals(expected, preset.InstructionSha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Preset '{preset.PresetKey}' assembles a different instruction than the one this edit was queued "
                + "with, so the preset's wording changed after the edit was queued. Queue the edit again.");
        }

        if (!string.Equals(instruction, image.Prompt, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The queued preset edit's prompt is not the instruction its preset assembles.");
        }

        using var provenance = JsonDocument.Parse(image.SourceProvenanceJson!);
        var sourceSha256 = provenance.RootElement.GetProperty("sourceImageSha256").GetString()
            ?? throw new InvalidOperationException("Preset edit provenance is missing the source checksum.");

        var source = await _assets.GetImageAsync(image.SourceImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Source scene asset image '{image.SourceImageId}' was not found.");
        if (!string.Equals(source.AssetId, image.AssetId, StringComparison.Ordinal)
            || source.Status != SceneAssetStatus.Complete
            || string.IsNullOrWhiteSpace(source.FileRelativePath))
        {
            throw new InvalidOperationException("The source asset image is not complete, stored, and owned by the queued asset.");
        }

        var applications = string.IsNullOrWhiteSpace(context.ReferenceApplicationsJson)
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<ReferenceApplicationSelection>>(
                context.ReferenceApplicationsJson, JsonOptions)
                ?? throw new InvalidOperationException("Preset edit reference applications are invalid.");
        var references = await _references.ResolveAsync(
            context.ExplicitEditorModelId, applications, ReferenceStrategyCatalogue.ReferenceImageSurface.Edit, cancellationToken);

        var sourceFileRelativePath = source.FileRelativePath;
        return new MediaEditRunPlan(
            image.Id,
            source.Id,
            token => _storage.OpenReadAsync(sourceFileRelativePath, token),
            sourceSha256,
            MediaEditOperation.ForEdit,
            Prompt: instruction,
            References: references,
            Editor: new MediaEditEditorResolution(context.ExplicitEditorModelId, RequiresAdultContentPolicy: false),
            LogScope: $"AssetId={image.AssetId}, Preset={preset.PresetKey}");
    }

    /// <summary>
    /// The multi-angle twin of the preset stage: the row's prompt is the instruction a picked pose assembled from the
    /// editor LoRA's <c>&lt;sks&gt;</c> grammar, and this re-derives it from the recorded pose to prove it is the same text.
    /// </summary>
    private async Task<MediaEditRunPlan> PrepareMultiAngleAsync(
        SceneAssetImage image,
        MediaEditRunContext context,
        MediaEditMultiAngleInstruction pose,
        CancellationToken cancellationToken)
    {
        var instruction = MultiAngleCameraInstructionComposer.Compose(pose.Azimuth, pose.Elevation, pose.Distance);

        var expected = MediaEditMultiAngleProvenance.InstructionSha256(instruction);
        if (!string.Equals(expected, pose.InstructionSha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The multi-angle pose assembles a different instruction than the one this edit was queued with, so the "
                + "pose's wording changed after the edit was queued. Queue the edit again.");
        }

        if (!string.Equals(instruction, image.Prompt, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The queued multi-angle edit's prompt is not the instruction its pose assembles.");
        }

        using var provenance = JsonDocument.Parse(image.SourceProvenanceJson!);
        var sourceSha256 = provenance.RootElement.GetProperty("sourceImageSha256").GetString()
            ?? throw new InvalidOperationException("Multi-angle edit provenance is missing the source checksum.");

        var source = await _assets.GetImageAsync(image.SourceImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Source scene asset image '{image.SourceImageId}' was not found.");
        if (!string.Equals(source.AssetId, image.AssetId, StringComparison.Ordinal)
            || source.Status != SceneAssetStatus.Complete
            || string.IsNullOrWhiteSpace(source.FileRelativePath))
        {
            throw new InvalidOperationException("The source asset image is not complete, stored, and owned by the queued asset.");
        }

        var applications = string.IsNullOrWhiteSpace(context.ReferenceApplicationsJson)
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<ReferenceApplicationSelection>>(
                context.ReferenceApplicationsJson, JsonOptions)
                ?? throw new InvalidOperationException("Multi-angle edit reference applications are invalid.");
        var references = await _references.ResolveAsync(
            context.ExplicitEditorModelId, applications, ReferenceStrategyCatalogue.ReferenceImageSurface.Edit, cancellationToken);

        var sourceFileRelativePath = source.FileRelativePath;
        return new MediaEditRunPlan(
            image.Id,
            source.Id,
            token => _storage.OpenReadAsync(sourceFileRelativePath, token),
            sourceSha256,
            MediaEditOperation.ForEdit,
            Prompt: instruction,
            References: references,
            Editor: new MediaEditEditorResolution(context.ExplicitEditorModelId, RequiresAdultContentPolicy: false),
            LogScope: $"AssetId={image.AssetId}, Azimuth={pose.Azimuth}, Elevation={pose.Elevation}, Distance={pose.Distance}");
    }

    /// <summary>
    /// The asset-store twin of the scene identity stage: the row's prompt is the service-authored face-only
    /// instruction and its references are the approved identity-pack faces recorded on the row. There is no
    /// compiler artifact to validate, so the checks are the source's completeness and checksum plus the exact
    /// file metadata of every reference.
    /// </summary>
    private async Task<MediaEditRunPlan> PrepareIdentityAsync(
        SceneAssetImage image,
        MediaEditRunContext context,
        IReadOnlyList<MediaEditIdentityBinding> bindings,
        CancellationToken cancellationToken)
    {
        var identityStorage = _identityStorage
            ?? throw new InvalidOperationException("Asset identity editing requires identity asset storage.");
        if (string.IsNullOrWhiteSpace(context.ExplicitEditorModelId))
            throw new InvalidOperationException("Asset identity editing requires the editor model chosen in the editor form.");
        if (string.IsNullOrWhiteSpace(image.Prompt))
            throw new InvalidOperationException("Asset identity editing requires the queued row's identity instruction.");
        if (string.IsNullOrWhiteSpace(image.SourceImageId))
            throw new InvalidOperationException("Asset identity editing requires the queued row to name its source image.");

        var source = await _assets.GetImageAsync(image.SourceImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Source scene asset image '{image.SourceImageId}' was not found.");
        if (!string.Equals(source.AssetId, image.AssetId, StringComparison.Ordinal)
            || source.Status != SceneAssetStatus.Complete
            || string.IsNullOrWhiteSpace(source.FileRelativePath))
        {
            throw new InvalidOperationException("The source asset image is not complete, stored, and owned by the queued asset.");
        }

        using var provenance = JsonDocument.Parse(image.SourceProvenanceJson!);
        var sourceSha256 = provenance.RootElement.GetProperty(MediaEditProvenance.SourceShaKey).GetString()
            ?? throw new InvalidOperationException("Asset identity provenance is missing the source checksum.");
        if (string.IsNullOrWhiteSpace(source.Sha256)
            || !string.Equals(source.Sha256, sourceSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The source asset image checksum changed after the identity edit was queued.");
        }

        var sourceFileRelativePath = source.FileRelativePath;
        var references = new List<MediaEditReference>(bindings.Count);
        foreach (var binding in bindings)
        {
            var relativePath = binding.FileRelativePath;
            references.Add(new MediaEditReference(
                binding.Ordinal,
                $"selected face identity reference for {binding.CharacterName} (CharacterFace:{binding.CharacterId})",
                $"{binding.CharacterId}.png",
                binding.Sha256,
                token => identityStorage.OpenReadAsync(relativePath, token)));
        }

        return new MediaEditRunPlan(
            image.Id,
            source.Id,
            token => _storage.OpenReadAsync(sourceFileRelativePath, token),
            sourceSha256,
            MediaEditOperation.ForEdit,
            Prompt: image.Prompt,
            References: references,
            Editor: new MediaEditEditorResolution(context.ExplicitEditorModelId, RequiresAdultContentPolicy: false),
            LogScope: $"AssetId={image.AssetId}, Stage=Identity");
    }

    /// <summary>
    /// A scene asset has no claim transition: its status is Pending, Complete or Failed, and it completes
    /// through an unguarded upsert (<c>UpsertImageAsync</c>), which is why asset edits could always be
    /// recorded. So there is nothing to claim and nothing that can refuse the run — inventing a status the
    /// store does not have would be worse than claiming nothing.
    /// </summary>
    public Task<bool> ClaimAsync(MediaEditRunContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    /// <summary>
    /// Prepares a deterministic operation on an asset image. The only things it needs are the queued
    /// image row, its complete stored source, and the source's stored checksum — there is no compiler
    /// artifact to validate, because the operation was never compiled.
    /// </summary>
    private async Task<MediaEditRunPlan?> PrepareOperationAsync(
        MediaEditRunContext context, CancellationToken cancellationToken)
    {
        // A confined edit is an EDIT that carries geometry: it needs the compiled prompt revision, the chosen editor
        // model and a mask, and none of them are resolved here. PrepareAsync routes both confined kinds, and this guard
        // is what keeps a future change to that routing from quietly queueing a run that dies at the handler.
        if (context.Operation.Kind is MediaEditOperationKind.MaskedRegion or MediaEditOperationKind.Outpaint)
        {
            throw new InvalidOperationException(
                $"A {context.Operation.Kind} edit is not a deterministic operation: it must be prepared on the "
                + "compiled-edit path, where its prompt revision, references and editor model are resolved.");
        }

        var image = await _assets.GetImageAsync(context.ImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Asset edit image '{context.ImageId}' was not found.");

        // A redelivered job must not re-run an operation that already finished.
        if (image.Status == SceneAssetStatus.Complete)
            return null;

        if (string.IsNullOrWhiteSpace(image.SourceImageId))
            throw new InvalidOperationException(
                $"An asset {context.Operation.Kind} requires the queued image to name its source image.");

        var source = await _assets.GetImageAsync(image.SourceImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Source scene asset image '{image.SourceImageId}' was not found.");
        if (!string.Equals(source.AssetId, image.AssetId, StringComparison.Ordinal)
            || source.Status != SceneAssetStatus.Complete
            || string.IsNullOrWhiteSpace(source.FileRelativePath))
        {
            throw new InvalidOperationException(
                "The source asset image is not complete, stored, and owned by the queued asset.");
        }

        if (string.IsNullOrWhiteSpace(source.Sha256))
            throw new InvalidOperationException("The source asset image has no stored checksum.");

        var sourceFileRelativePath = source.FileRelativePath;
        return new MediaEditRunPlan(
            image.Id,
            source.Id,
            token => _storage.OpenReadAsync(sourceFileRelativePath, token),
            source.Sha256,
            context.Operation,
            LogScope: $"AssetId={image.AssetId}");
    }

    public async Task CompleteAsync(
        MediaEditRunPlan plan, MediaEditRunOutput output, CancellationToken cancellationToken = default)
    {
        var image = await _assets.GetImageAsync(plan.ImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Asset edit image '{plan.ImageId}' was not found.");

        await using var content = new MemoryStream(output.Bytes);
        var stored = await _storage.SaveAsync($"{image.Id}.png", content, cancellationToken);

        if (output.Operation != MediaEditOperationKind.Edit)
        {
            // A crop or an enhance is not a render, so the row must not claim a model produced it.
            image.ModelSnapshotJson = null;
        }
        else
        {
            var editor = plan.Editor
                ?? throw new InvalidOperationException("An edit run requires the editor resolution its plan carries.");
            image.ModelSnapshotJson = JsonSerializer.Serialize(
                new { requestedModelId = editor.ExplicitModelId, output.ModelIdentifier, output.ProviderName }, JsonOptions);
        }

        image.Status = SceneAssetStatus.Complete;
        image.FileRelativePath = stored.RelativePath;
        image.MediaType = stored.MediaType;
        image.Width = stored.Width;
        image.Height = stored.Height;
        image.ByteLength = stored.ByteLength;
        image.Sha256 = stored.Sha256;
        image.ErrorMessage = null;
        image.CompletedUtc = DateTime.UtcNow;
        image.UpdatedUtc = image.CompletedUtc.Value;
        await _assets.UpsertImageAsync(image, cancellationToken);

        if (TryReadEditSessionId(image.SourceProvenanceJson, out var editSessionId))
        {
            await _edits.UpdateSessionStatusAsync(
                editSessionId, SceneAssetImageEditSessionStatus.Completed, DateTime.UtcNow, image.CompletedUtc, cancellationToken);
        }
    }

    public async Task FailAsync(
        MediaEditRunContext context, string error, CancellationToken cancellationToken = default)
    {
        var image = await _assets.GetImageAsync(context.ImageId, cancellationToken);
        if (image is null)
            return;

        image.Status = SceneAssetStatus.Failed;
        image.ErrorMessage = error;
        image.UpdatedUtc = DateTime.UtcNow;
        await _assets.UpsertImageAsync(image, cancellationToken);

        if (TryReadEditSessionId(image.SourceProvenanceJson, out var editSessionId)
            && await _edits.GetLatestAttemptAsync(editSessionId, cancellationToken) is not null)
        {
            await _edits.UpdateSessionStatusAsync(
                editSessionId, SceneAssetImageEditSessionStatus.Failed, DateTime.UtcNow, cancellationToken: cancellationToken);
        }
    }

    private static bool TryReadEditSessionId(string? provenanceJson, out string editSessionId)
    {
        editSessionId = string.Empty;
        if (string.IsNullOrWhiteSpace(provenanceJson))
            return false;

        using var provenance = JsonDocument.Parse(provenanceJson);
        var value = provenance.RootElement.TryGetProperty("editSessionId", out var element)
            ? element.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        editSessionId = value;
        return true;
    }
}
