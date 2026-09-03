using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed record IdentityRequestCompilationInput(
    SceneImageRecord Image,
    string PositivePrompt,
    string NegativePrompt,
    long? Seed);

public sealed record IdentityReferenceAudit(
    string PackId,
    int PackVersion,
    string CharacterLabel,
    string FaceAssetId,
    string ReferenceSha256,
    int ReferenceBytes,
    double? Strength,
    SceneImageEditTargetRegion? Region);

public sealed record CompiledIdentityRequest(
    IdentityControlledImageRequest Request,
    IReadOnlyList<IdentityReferenceAudit> References);

public interface IIdentityControlledRequestCompiler
{
    Task<CompiledIdentityRequest> CompileAsync(
        IdentityRequestCompilationInput input,
        CancellationToken cancellationToken = default);
}

public sealed class IdentityControlledRequestCompiler : IIdentityControlledRequestCompiler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ICharacterImageIdentityRepository _identityRepository;
    private readonly ICharacterImageAssetStorageService _identityStorage;

    public IdentityControlledRequestCompiler(
        ICharacterImageIdentityRepository identityRepository,
        ICharacterImageAssetStorageService identityStorage)
    {
        _identityRepository = identityRepository;
        _identityStorage = identityStorage;
    }

    public async Task<CompiledIdentityRequest> CompileAsync(
        IdentityRequestCompilationInput input,
        CancellationToken cancellationToken = default)
    {
        var selections = DeserializePackSelections(input.Image);
        if (selections.Count == 0)
            throw new InvalidOperationException("Identity-controlled rendering requires at least one approved identity pack.");

        var references = new List<IdentityReferenceInput>(selections.Count);
        var audits = new List<IdentityReferenceAudit>(selections.Count);
        foreach (var selection in selections)
        {
            var pack = await _identityRepository.GetPackAsync(selection.PackId, cancellationToken)
                ?? throw new InvalidOperationException($"Identity pack '{selection.PackId}' was not found.");
            if (pack.Status != CharacterImageIdentityPackStatus.Approved)
            {
                throw new InvalidOperationException(
                    $"Identity pack '{selection.PackId}' is not approved; only approved packs can be used for identity-controlled rendering.");
            }
            if (string.IsNullOrWhiteSpace(pack.CanonicalFaceAssetId))
                throw new InvalidOperationException($"Identity pack '{selection.PackId}' has no canonical face asset.");

            var face = await _identityRepository.GetAssetAsync(pack.CanonicalFaceAssetId, cancellationToken)
                ?? throw new InvalidOperationException($"Canonical face asset '{pack.CanonicalFaceAssetId}' was not found.");
            if (!string.Equals(face.IdentityPackId, pack.Id, StringComparison.Ordinal)
                || face.AssetKind != SceneImageReferenceAssetKind.Face
                || !face.IsApproved)
            {
                throw new InvalidOperationException(
                    $"Canonical face asset '{face.Id}' must be an approved face owned by identity pack '{pack.Id}'.");
            }

            byte[] referenceBytes;
            await using (var source = await _identityStorage.OpenReadAsync(face.FileRelativePath, cancellationToken))
            using (var buffer = new MemoryStream())
            {
                await source.CopyToAsync(buffer, cancellationToken);
                referenceBytes = buffer.ToArray();
            }
            if (referenceBytes.Length == 0)
                throw new InvalidOperationException($"Canonical face asset '{face.Id}' contains no image bytes.");

            var characterLabel = string.IsNullOrWhiteSpace(selection.CharacterLabel) ? pack.Id : selection.CharacterLabel;
            references.Add(new IdentityReferenceInput
            {
                CharacterLabel = characterLabel,
                ReferenceImageBytes = referenceBytes,
                StrengthOverride = selection.Strength,
                Region = selection.Region is null
                    ? null
                    : new IdentityReferenceRegion
                    {
                        X = selection.Region.X,
                        Y = selection.Region.Y,
                        Width = selection.Region.Width,
                        Height = selection.Region.Height
                    }
            });
            audits.Add(new IdentityReferenceAudit(
                pack.Id,
                pack.Version,
                characterLabel,
                face.Id,
                face.Sha256,
                referenceBytes.Length,
                selection.Strength,
                selection.Region));
        }

        var request = new IdentityControlledImageRequest
        {
            PositivePrompt = input.PositivePrompt,
            NegativePrompt = input.NegativePrompt,
            Size = input.Image.ImageSize,
            Seed = input.Seed,
            References = references,
            CorrelationId = input.Image.Id
        };
        if (references.Count == 1)
            request.ReferenceImageBytes = references[0].ReferenceImageBytes;

        return new CompiledIdentityRequest(request, audits);
    }

    private static List<IdentityPackSelection> DeserializePackSelections(SceneImageRecord image)
    {
        if (!string.IsNullOrWhiteSpace(image.IdentityPacksJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<IdentityPackSelection>>(image.IdentityPacksJson, JsonOptions);
                return parsed is { Count: > 0 }
                    ? parsed
                    : throw new InvalidOperationException("Identity pack selections must contain at least one exact pack version.");
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("Persisted identity pack selections are malformed.", exception);
            }
        }

        return string.IsNullOrWhiteSpace(image.IdentityPackId)
            ? []
            : [new IdentityPackSelection { PackId = image.IdentityPackId }];
    }
}